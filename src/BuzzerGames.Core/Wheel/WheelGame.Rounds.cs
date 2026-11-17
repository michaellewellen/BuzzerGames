using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.Wheel;

// The two rounds that sit around the regular puzzles: the quick rounds ("hit the buzzer" toss-ups) and the bonus round at the end.
// The game is a plan, a list of steps played one after another: two regular puzzles, a quick round of three, two more regular puzzles,
// the second quick round, the last two regular puzzles, then the bonus. A quick round or the bonus that the puzzle bank can't supply is
// simply left out, so a thin bank never stops a game from starting.

public sealed partial class WheelGame
{
    private enum StepKind { Regular, Quick, Bonus }

    private readonly record struct Step(StepKind Kind, int Index, int Sub = 0);

    private List<Step> _plan = [];
    private List<(string Theme, List<BoardLayout> Boards)> _quickSets = [];
    private List<(WheelPuzzle Puzzle, BoardLayout Board)> _bonusChoices = [];

    private bool InQuick => _plan.Count > _s.Step && _plan[_s.Step].Kind == StepKind.Quick;
    private bool InBonus => _plan.Count > _s.Step && _plan[_s.Step].Kind == StepKind.Bonus;

    private void BuildPlan(WheelDeal deal)
    {
        _quickSets = [];
        foreach (var set in deal.Quick)
        {
            var boards = new List<BoardLayout>();
            foreach (var text in set.Texts)
                if (WheelBoard.TryLayout(text, out var layout, out _)) boards.Add(layout);
            if (boards.Count == WheelRules.QuickPrizes.Count) _quickSets.Add((set.Theme, boards));
        }

        _bonusChoices = [];
        foreach (var puzzle in deal.BonusChoices)
            if (WheelBoard.TryLayout(puzzle.Text, out var layout, out _)) _bonusChoices.Add((puzzle, layout));

        _plan = [];
        for (var number = 1; number <= WheelRules.RegularPuzzles; number++)
        {
            _plan.Add(new Step(StepKind.Regular, number));
            var set = number / 2 - 1;   // after puzzle 2 comes the first quick round, after puzzle 4 the second
            if (number % 2 == 0 && set < _quickSets.Count && set < 2)
                for (var sub = 0; sub < WheelRules.QuickPrizes.Count; sub++) _plan.Add(new Step(StepKind.Quick, set, sub));
        }
        if (_bonusChoices.Count > 0) _plan.Add(new Step(StepKind.Bonus, 0));
    }

    private void BeginStep()
    {
        var step = _plan[_s.Step];
        switch (step.Kind)
        {
            case StepKind.Regular: StartPuzzle(step.Index); break;
            case StepKind.Quick: StartQuick(step); break;
            default: StartBonus(); break;
        }
    }

    private string StepLabel()
    {
        if (_plan.Count <= _s.Step || _s.Phase == WheelPhase.Lobby) return "";
        var step = _plan[_s.Step];
        return step.Kind switch
        {
            StepKind.Regular => $"Puzzle {step.Index} of {WheelRules.RegularPuzzles}",
            StepKind.Quick => $"Toss-up {step.Sub + 1} of {WheelRules.QuickPrizes.Count}",
            _ => "Bonus round"
        };
    }

    // ---- Quick rounds --------------------------------------------------------------------

    private void StartQuick(Step step)
    {
        var s = _s;
        var (theme, boards) = _quickSets[step.Index];
        s.Board = boards[step.Sub];
        s.Category = theme;
        s.Tiles = s.Board.Tiles.Select(t => t.IsLetter ? TileState.Hidden : TileState.Revealed).ToArray();
        s.Called = [];
        s.Lit = [];
        s.Staged = null;
        s.Spin = null;
        s.SpinValue = 0;
        s.RoundBank = new int[WheelRules.Players];
        s.MillionHolder = -1;
        s.PuzzleWinner = -1;
        s.LockedOut = new bool[WheelRules.Players];
        s.QuickPrize = WheelRules.QuickPrizes[step.Sub];
        s.QuickIdle = 0;
        s.Active = -1;
        s.Phase = WheelPhase.QuickReady;
        s.Message = $"Toss-up for ${s.QuickPrize:N0}! The theme is {theme}.";
    }

    /// <summary>The emcee starts the toss-up: letters begin to appear and everyone's buzzer is live.</summary>
    public Result StartQuickRound(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.QuickReady) return Result.Fail("There's no toss-up waiting to start.");
        Remember();
        _s.Phase = WheelPhase.QuickPlay;
        _lastTickMs = Environment.TickCount64;
        _s.Message = "Hands on your buzzers!";
        return Result.Success;
    });

    /// <summary>
    /// Shows one more letter, picked at random. The big screen calls this about once a second while the toss-up is on (the emcee has a button
    /// for it too, if the screen isn't there). Calls that come too soon are ignored, so two callers can't make the letters come twice as fast.
    /// Once every letter is showing and nobody has solved it, a few more ticks end the puzzle.
    /// </summary>
    public Result QuickTick() => Run(() =>
    {
        if (_s.Phase != WheelPhase.QuickPlay) return Result.Fail("No toss-up is running.");
        var now = Environment.TickCount64;
        if (now - _lastTickMs < _quickTickMillis) return Result.Fail("Too soon for the next letter.");
        _lastTickMs = now;

        var hidden = Enumerable.Range(0, _s.Tiles.Length).Where(i => _s.Tiles[i] == TileState.Hidden && _s.Board!.Tiles[i].IsLetter).ToList();
        if (hidden.Count > 0)
        {
            _s.Tiles[hidden[_rng.Next(hidden.Count)]] = TileState.Revealed;
            return Result.Success;
        }
        if (++_s.QuickIdle >= WheelRules.QuickIdleTicks)
        {
            RevealEverything();
            _s.Phase = WheelPhase.PuzzleOver;
            _s.Message = "Time's up. Nobody got it.";
        }
        return Result.Success;
    });

    /// <summary>A player hits their buzzer (from their own phone).</summary>
    public Result Buzz(Guid token) => Run(() =>
    {
        var index = _players.FindIndex(p => p.Token == token);
        return index < 0 ? Result.Fail("Only the players can buzz in.") : BuzzIn(index);
    });

    /// <summary>The emcee buzzes in for a player whose phone isn't working.</summary>
    public Result BuzzFor(Guid emceeToken, int player) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (player < 0 || player >= _players.Count) return Result.Fail("There's no such player.");
        return BuzzIn(player);
    });

    private Result BuzzIn(int index)
    {
        if (_s.Phase != WheelPhase.QuickPlay) return Result.Fail("It isn't time to buzz in.");
        if (_s.LockedOut[index]) return Result.Fail("You're locked out of this puzzle.");
        Remember();
        _s.Active = index;
        _s.Phase = WheelPhase.QuickAnswer;
        _s.Message = $"{_players[index].Name} buzzed in!";
        return Result.Success;
    }

    /// <summary>
    /// The emcee's ruling on a buzzed-in answer. Right: the whole board shows and the prize is theirs (it moves into their total when the
    /// emcee continues, like a regular puzzle). Wrong: that buzzer is dead for the rest of this puzzle and the letters carry on.
    /// </summary>
    public Result JudgeQuick(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.QuickAnswer) return Result.Fail("Nobody has buzzed in.");

        Remember();
        var index = _s.Active;
        var name = _players[index].Name;
        if (correct)
        {
            _s.RoundBank[index] = _s.QuickPrize;
            _s.PuzzleWinner = index;
            RevealEverything();
            _s.Phase = WheelPhase.PuzzleOver;
            _s.Message = $"{name} solved it and wins ${_s.QuickPrize:N0}!";
            return Result.Success;
        }

        _s.LockedOut[index] = true;
        _s.Active = -1;
        if (_s.LockedOut.All(x => x))
        {
            RevealEverything();
            _s.Phase = WheelPhase.PuzzleOver;
            _s.Message = "Everyone is locked out. Nobody got it.";
            return Result.Success;
        }
        _s.Phase = WheelPhase.QuickPlay;
        _lastTickMs = Environment.TickCount64;
        _s.Message = $"Sorry, {name}, that's not it. You're locked out.";
        return Result.Success;
    });

    // ---- The bonus round -----------------------------------------------------------------

    private void StartBonus()
    {
        var s = _s;
        var best = s.Total.Max();
        var leaders = Enumerable.Range(0, WheelRules.Players).Where(i => s.Total[i] == best).ToList();
        var player = leaders[_rng.Next(leaders.Count)];

        var cards = WheelRules.BonusPrizes.OrderBy(_ => _rng.Next()).ToArray();
        if (s.MillionWon[player]) cards[_rng.Next(cards.Length)] = WheelRules.MillionCardValue;   // the wedge pays off here

        s.BonusPlayer = player;
        s.Active = player;
        s.BonusCards = cards;
        s.BonusPicked = -1;
        s.BonusChosen = -1;
        s.BonusStartedUtc = null;
        s.BonusTimeUp = false;
        s.Board = null;
        s.Category = "";
        s.Tiles = [];
        s.Called = [];
        s.Lit = [];
        s.Staged = null;
        s.Spin = null;
        s.SpinValue = 0;
        s.RoundBank = new int[WheelRules.Players];
        s.MillionHolder = -1;
        s.PuzzleWinner = -1;
        s.Phase = WheelPhase.BonusPick;
        s.Message = leaders.Count > 1
            ? $"{_players[player].Name} wins the tie-break and plays the bonus round! Pick a card."
            : $"{_players[player].Name} has the most money and plays the bonus round! Pick a card.";
    }

    /// <summary>The bonus player picks a face-down card (the emcee can pick for them, or a random one when <paramref name="card"/> is -1). Nobody sees what is on it yet.</summary>
    public Result PickCard(Guid token, int card) => Run(() =>
    {
        var isPlayer = _s.BonusPlayer >= 0 && _players[_s.BonusPlayer].Token == token;
        if (!isPlayer && !IsEmcee(token)) return Result.Fail("Only the bonus player picks the card.");
        if (_s.Phase != WheelPhase.BonusPick) return Result.Fail("It isn't time to pick a card.");
        if (card == -1 && !IsEmcee(token)) return Result.Fail("Pick one of the cards.");
        if (card < -1 || card >= WheelRules.BonusCards) return Result.Fail($"The cards are numbered 1 to {WheelRules.BonusCards}.");

        Remember();
        _s.BonusPicked = card == -1 ? _rng.Next(WheelRules.BonusCards) : card;
        _s.Phase = WheelPhase.BonusCategory;
        _s.Message = $"Card {_s.BonusPicked + 1} is {_players[_s.BonusPlayer].Name}'s. Now choose a category.";
        return Result.Success;
    });

    /// <summary>The emcee taps the category the bonus player chose. That puzzle goes up with R S T L N E already showing.</summary>
    public Result ChooseBonusCategory(Guid emceeToken, int choice) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.BonusCategory) return Result.Fail("It isn't time to choose a category.");
        if (choice < 0 || choice >= _bonusChoices.Count) return Result.Fail("There's no such category.");

        Remember();
        var (puzzle, layout) = _bonusChoices[choice];
        _s.BonusChosen = choice;
        _s.Board = layout;
        _s.Category = puzzle.Category;
        _s.Called = [.. WheelRules.BonusGivenLetters];
        _s.Tiles = layout.Tiles.Select(t => !t.IsLetter || WheelRules.BonusGivenLetters.Contains(t.Letter) ? TileState.Revealed : TileState.Hidden).ToArray();
        _s.BonusConsonantsLeft = WheelRules.BonusConsonants;
        _s.BonusVowelsLeft = WheelRules.BonusVowels;
        _s.Phase = WheelPhase.BonusLetters;
        _s.Message = BonusLettersMessage();
        return Result.Success;
    });

    private string BonusLettersMessage() =>
        $"{WheelRules.BonusGivenLetters} are in. Call {_s.BonusConsonantsLeft} more consonant{(_s.BonusConsonantsLeft == 1 ? "" : "s")} and {_s.BonusVowelsLeft} vowel{(_s.BonusVowelsLeft == 1 ? "" : "s")}.";

    private Result ConfirmBonusLetter(char letter)
    {
        Remember();
        _s.Staged = null;
        _s.Called.Add(letter);
        if (IsVowel(letter)) _s.BonusVowelsLeft--; else _s.BonusConsonantsLeft--;

        var hits = new List<int>();
        for (var i = 0; i < _s.Board!.Tiles.Count; i++)
            if (_s.Board.Tiles[i].Letter == letter && _s.Tiles[i] == TileState.Hidden) hits.Add(i);

        if (hits.Count == 0)
        {
            AfterBonusLetter(prefix: $"No {letter}. ");
            return Result.Success;
        }
        foreach (var i in hits) _s.Tiles[i] = TileState.Lit;
        _s.Lit = hits;
        _s.Phase = WheelPhase.Reveal;
        _s.Message = $"{hits.Count} {letter}{(hits.Count == 1 ? "" : "'s")}!";
        return Result.Success;
    }

    /// <summary>After each bonus letter: more to call, or all four are in and the clock can start.</summary>
    private void AfterBonusLetter(string prefix = "")
    {
        if (_s.BonusConsonantsLeft == 0 && _s.BonusVowelsLeft == 0)
        {
            _s.Phase = WheelPhase.BonusReady;
            _s.Message = prefix + "That's all the letters. Ready when you are!";
        }
        else
        {
            _s.Phase = WheelPhase.BonusLetters;
            _s.Message = prefix + BonusLettersMessage();
        }
    }

    /// <summary>The emcee says "your time starts now": the clock runs for <see cref="WheelRules.BonusSeconds"/> seconds.</summary>
    public Result StartBonusClock(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.BonusReady) return Result.Fail("The letters aren't all in yet.");
        Remember();
        _s.BonusStartedUtc = DateTime.UtcNow;
        _s.BonusTimeUp = false;
        _s.Phase = WheelPhase.BonusSolve;
        _s.Message = "Your time starts now!";
        return Result.Success;
    });

    /// <summary>The big screen calls this when the clock reaches zero. It only says so: the emcee still rules on whatever the player said.</summary>
    public Result BonusTimeUp() => Run(() =>
    {
        if (_s.Phase != WheelPhase.BonusSolve || _s.BonusStartedUtc is not { } started) return Result.Fail("No clock is running.");
        if (_s.BonusTimeUp) return Result.Fail("The time was already called.");
        if ((DateTime.UtcNow - started).TotalSeconds < WheelRules.BonusSeconds - 1) return Result.Fail("There is still time on the clock.");
        _s.BonusTimeUp = true;
        _s.Message = "Time's up!";
        return Result.Success;
    });

    /// <summary>The emcee's ruling on the bonus answer. Either way the board shows and the card is turned over; only a right answer pays.</summary>
    public Result JudgeBonus(Guid emceeToken, bool correct) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_s.Phase != WheelPhase.BonusSolve) return Result.Fail("The clock isn't running.");

        Remember();
        var index = _s.BonusPlayer;
        var name = _players[index].Name;
        var prize = _s.BonusCards[_s.BonusPicked];
        RevealEverything();
        _s.Phase = WheelPhase.BonusOver;
        if (correct)
        {
            _s.RoundBank[index] = prize;       // moves into their total when the emcee finishes the game, like any other win
            _s.PuzzleWinner = index;
            _s.Message = prize == WheelRules.MillionCardValue
                ? $"{name} solved it and wins THE MILLION DOLLARS!"
                : $"{name} solved it and wins ${prize:N0}!";
        }
        else
        {
            _s.Message = $"Sorry, {name}. The card was worth ${prize:N0}.";
        }
        return Result.Success;
    });

    private WheelBonusInfo? BonusInfo()
    {
        if (!InBonus) return null;
        var s = _s;
        double? left = s.Phase == WheelPhase.BonusSolve && s.BonusStartedUtc is { } started
            ? Math.Max(0, WheelRules.BonusSeconds - (DateTime.UtcNow - started).TotalSeconds)
            : null;
        return new WheelBonusInfo(
            s.BonusPicked,
            s.Phase == WheelPhase.BonusCategory ? _bonusChoices.Select(c => c.Puzzle.Category).ToList() : [],
            s.Phase == WheelPhase.BonusOver ? s.BonusCards[s.BonusPicked] : null,
            left,
            s.BonusTimeUp,
            s.BonusConsonantsLeft,
            s.BonusVowelsLeft);
    }
}
