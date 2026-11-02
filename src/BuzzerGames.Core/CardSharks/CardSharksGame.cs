using BuzzerGames.Core.Jeopardy;

namespace BuzzerGames.Core.CardSharks;

/// <summary>
/// One Card Sharks match: an emcee and two teams, A and B. The teams never log in; they play out loud, and the emcee runs everything
/// from their phone. A match is the best of three games.
///
/// Each team has its own shuffled 52-card deck and a row of seven cards (the base card and six more); the third, tiebreaker game uses four.
/// A game has four questions. For each, one team guesses a number from 0 to 100 (the real result of a survey) and the other calls higher or lower.
/// A right call wins control; otherwise the guesser keeps it. The team in control turns its base card, may swap it once, then plays higher or lower
/// on each next card. A wrong call (a tie is wrong) removes every card turned since its last freeze, and then the other team plays its own row, but cannot swap.
/// A team that has made a correct call this turn may freeze, which locks in its progress and ends its turn. Completing a row wins the game.
/// The fourth question is sudden death: the team that wins control chooses who finishes a row, with no freezing; finishing wins, failing loses.
///
/// Like the other games, this class knows nothing about web pages, and every public method takes one lock and returns a Result.
/// </summary>
public sealed class CardSharksGame
{
    public const int Teams = 2;
    public const int NameMaxLength = 20;
    public const int QuestionsPerGame = 4;
    public const int GamesToWin = 2;
    public const int FullRow = 7;
    public const int TiebreakRow = 4;

    private sealed class Row
    {
        public List<Card> Cards { get; } = [];
        public int Frozen { get; set; } = -1;
        public bool Failed { get; set; }
    }

    private readonly object _gate = new();
    private readonly ICardSharksContent _content;
    private readonly Random _rng;
    private readonly Func<int, IReadOnlyList<Card>>? _deckFactory;

    private Guid? _emceeToken;
    private string? _emceeName;
    private CardPhase _phase = CardPhase.Lobby;
    private string? _message;
    private readonly string[] _teamNames = ["Team A", "Team B"];
    private readonly int[] _gamesWon = new int[Teams];

    private int _gameNumber;
    private int _rowLength = FullRow;
    private int _question;
    private readonly Row[] _rows = [new(), new()];
    private readonly Stack<Card>[] _decks = [new(), new()];

    private List<CardQuestion> _questionDeck = [];
    private int _questionPos;
    private string _questionText = "";
    private int _answer;
    private bool _answerShown;
    private int? _guess;
    private int _guesser = -1;
    private bool? _calledHigher;
    private int _control = -1;
    private int _turn = -1;
    private bool _turnIsControl;
    private int _progress;
    private int _gameWinner = -1;
    private int _matchWinner = -1;

    // Money Cards
    private Stack<Card> _bonusDeck = new();
    private readonly List<List<Card>> _bonusRows = [];
    private int _bonusRow;
    private int _pot;
    private int _winnings = -1;

    public CardSharksGame(string code, ICardSharksContent content, Random? rng = null, Func<int, IReadOnlyList<Card>>? deckFactory = null)
    {
        Code = code;
        _content = content;
        _rng = rng ?? new Random();
        _deckFactory = deckFactory;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Code { get; }
    public DateTime CreatedAtUtc { get; }
    public event Action? Changed;

    // ---- The lobby -----------------------------------------------------------------------

    public JoinResult JoinEmcee(string name) => Run(() =>
    {
        if (_emceeToken is not null) return JoinResult.Fail("This game already has an emcee.");
        if (!TryCleanText(name, NameMaxLength, out var clean)) return JoinResult.Fail("Please enter a name.");
        _emceeToken = Guid.NewGuid();
        _emceeName = clean;
        return JoinResult.Success(_emceeToken.Value);
    });

    public Result SetTeamNames(Guid emceeToken, string? teamA, string? teamB) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Lobby) return Result.Fail("Team names can only change before the game starts.");
        if (!TryCleanText(teamA, NameMaxLength, out var a) || !TryCleanText(teamB, NameMaxLength, out var b))
            return Result.Fail("Each team needs a name.");
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return Result.Fail("The teams need different names.");
        _teamNames[0] = a;
        _teamNames[1] = b;
        return Result.Success;
    });

    public Result Start(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can start the game.");
        if (_phase != CardPhase.Lobby) return Result.Fail("The game has already started.");
        BeginMatch();
        return Result.Success;
    });

    /// <summary>After the match: play again with the same teams.</summary>
    public Result Restart(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase is not (CardPhase.MatchOver or CardPhase.BonusOver)) return Result.Fail("The match isn't over yet.");
        BeginMatch();
        return Result.Success;
    });

    // ---- The question --------------------------------------------------------------------

    /// <summary>The guessing team has said its number out loud; the emcee types it in.</summary>
    public Result TakeGuess(Guid emceeToken, int number) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Guess) return Result.Fail("It isn't time to guess a number.");
        if (number is < 0 or > 100) return Result.Fail("The number has to be from 0 to 100.");
        _guess = number;
        _phase = CardPhase.Call;
        _message = $"{_teamNames[_guesser]} guessed {number}. {_teamNames[1 - _guesser]}: is the real number higher or lower?";
        return Result.Success;
    });

    /// <summary>The other team calls higher or lower. The survey answer is revealed and control goes to whoever was right.</summary>
    public Result Call(Guid emceeToken, bool higher) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Call) return Result.Fail("It isn't time to call higher or lower.");
        _calledHigher = higher;
        _answerShown = true;
        var guess = _guess!.Value;
        // An exact hit is the guesser's: the other team called a direction that wasn't true.
        var callerRight = higher ? _answer > guess : _answer < guess;
        _control = callerRight ? 1 - _guesser : _guesser;
        _phase = CardPhase.Verdict;
        _message = $"The real answer: {_answer}. " + (_answer == guess
            ? $"Dead on! {_teamNames[_control]} takes control."
            : $"{_teamNames[_control]} takes control.");
        return Result.Success;
    });

    // ---- Playing the cards ---------------------------------------------------------------

    /// <summary>Moves on: from the verdict to the cards, from a finished turn to the next team or question, and from a game to the next.</summary>
    public Result Continue(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        switch (_phase)
        {
            case CardPhase.Verdict:
                if (IsSuddenDeath)
                {
                    _phase = CardPhase.SuddenPick;
                    _message = $"SUDDEN DEATH! {_teamNames[_control]} chooses: finish your own row, or make {_teamNames[1 - _control]} finish theirs. No freezing.";
                }
                else StartTurn(_control, isControl: true);
                return Result.Success;

            case CardPhase.TurnOver:
                var row = _rows[_turn];
                if (row.Failed)
                {
                    row.Cards.RemoveRange(row.Frozen + 1, row.Cards.Count - row.Frozen - 1);
                    row.Failed = false;
                    if (_turnIsControl)
                    {
                        StartTurn(1 - _control, isControl: false);
                        return Result.Success;
                    }
                }
                NextQuestion();
                return Result.Success;

            case CardPhase.GameOver:
                if (_gamesWon.Any(g => g >= GamesToWin))
                {
                    _matchWinner = _gamesWon[0] >= GamesToWin ? 0 : 1;
                    _phase = CardPhase.MatchOver;
                    _message = $"{_teamNames[_matchWinner]} wins the match, {_gamesWon[_matchWinner]} games to {_gamesWon[1 - _matchWinner]}! On to the big money.";
                }
                else
                {
                    _gameNumber++;
                    BeginGame();
                }
                return Result.Success;

            default:
                return Result.Fail("There's nothing to continue from.");
        }
    });

    /// <summary>The first card is showing for the team in control: keep it.</summary>
    public Result Keep(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Offer) return Result.Fail("There's no card to keep.");
        _phase = CardPhase.Play;
        _message = $"{_teamNames[_turn]}: higher or lower than the {_rows[_turn].Cards[0]}?";
        return Result.Success;
    });

    /// <summary>The first card is showing for the team in control: swap it for the top card of their deck. The swap is final.</summary>
    public Result Change(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Offer) return Result.Fail("There's no card to change.");
        _rows[_turn].Cards[0] = Draw(_turn);
        _phase = CardPhase.Play;
        _message = $"{_teamNames[_turn]}: higher or lower than the {_rows[_turn].Cards[0]}?";
        return Result.Success;
    });

    /// <summary>The team calls higher or lower and the next card is turned over. A tie loses.</summary>
    public Result HigherLower(Guid emceeToken, bool higher) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Play) return Result.Fail("There's no card to guess.");
        var row = _rows[_turn];
        var current = row.Cards[^1];
        var next = Draw(_turn);
        row.Cards.Add(next);
        var right = higher ? next.Rank > current.Rank : next.Rank < current.Rank;

        if (!right)
        {
            if (IsSuddenDeath)
            {
                row.Failed = true;
                WinGame(1 - _turn, $"The {next}! {_teamNames[_turn]} misses, so {_teamNames[1 - _turn]} wins the game!");
                return Result.Success;
            }
            row.Failed = true;
            _phase = CardPhase.TurnOver;
            _message = (next.Rank == current.Rank ? $"A tie, the {next}, and a tie loses! " : $"The {next}. Wrong! ")
                + (row.Frozen >= 0 ? $"{_teamNames[_turn]} falls back to its frozen card." : $"{_teamNames[_turn]} loses its cards.");
            return Result.Success;
        }

        _progress++;
        if (row.Cards.Count == _rowLength)
        {
            WinGame(_turn, $"The {next}! {_teamNames[_turn]} completes the row and wins the game!");
            return Result.Success;
        }
        _message = $"The {next}! Right. {_teamNames[_turn]}: higher or lower than the {next}?";
        return Result.Success;
    });

    /// <summary>The team locks in its progress and its turn ends. Needs at least one correct call this turn. Not allowed in sudden death.</summary>
    public Result Freeze(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.Play) return Result.Fail("There's nothing to freeze.");
        if (IsSuddenDeath) return Result.Fail("No freezing in sudden death.");
        if (_progress < 1) return Result.Fail("You have to get one right before you can freeze.");
        var row = _rows[_turn];
        row.Frozen = row.Cards.Count - 1;
        _phase = CardPhase.TurnOver;
        _message = $"{_teamNames[_turn]} freezes on the {row.Cards[^1]}!";
        return Result.Success;
    });

    /// <summary>Sudden death: the team in control chooses who finishes a row, itself or the other team.</summary>
    public Result ChooseFinisher(Guid emceeToken, bool controllingTeamPlays) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.SuddenPick) return Result.Fail("It isn't time to choose.");
        StartTurn(controllingTeamPlays ? _control : 1 - _control, isControl: controllingTeamPlays);
        return Result.Success;
    });

    // ---- Money Cards: the winning team's bonus round --------------------------------------

    public const int BonusStartingPot = 200;
    public const int BonusMinBet = 50;
    private static readonly int[] BonusRowPlays = [3, 3, 1];     // cards to turn over after the base card
    private static readonly int[] BonusRowPrizes = [0, 400, 1000];

    /// <summary>After the match, the winners play for money.</summary>
    public Result StartBonus(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.MatchOver) return Result.Fail("Money Cards is played after the match.");
        _bonusDeck = new Stack<Card>(NewDeck(2));
        _bonusRows.Clear();
        _bonusRow = 0;
        _pot = BonusStartingPot;
        _winnings = -1;
        _bonusRows.Add([DrawBonus()]);
        _phase = CardPhase.BonusOffer;
        _message = $"{_teamNames[_matchWinner]}, you start with ${_pot}. Your first card is the {_bonusRows[0][0]}. Keep it or change it?";
        return Result.Success;
    });

    /// <summary>Keep the row's base card.</summary>
    public Result BonusKeep(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.BonusOffer) return Result.Fail("There's no card to keep.");
        EnterBetting();
        return Result.Success;
    });

    /// <summary>Swap the row's base card for the top card of the deck. The swap is final.</summary>
    public Result BonusChange(Guid emceeToken) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.BonusOffer) return Result.Fail("There's no card to change.");
        _bonusRows[_bonusRow][0] = DrawBonus();
        EnterBetting();
        return Result.Success;
    });

    /// <summary>The smallest bet allowed right now: $50 (or whatever is left), and on the Big Bet row at least half the money.</summary>
    private int MinBetNow() => _bonusRow == 2 ? (_pot + 1) / 2 : Math.Min(BonusMinBet, _pot);

    /// <summary>The contestant bets and calls higher or lower. A tie loses.</summary>
    public Result BonusPlay(Guid emceeToken, int bet, bool higher) => Run(() =>
    {
        if (!IsEmcee(emceeToken)) return Result.Fail("Only the emcee can do that.");
        if (_phase != CardPhase.BonusBet) return Result.Fail("It isn't time to bet.");
        var min = MinBetNow();
        if (bet < min) return Result.Fail(_bonusRow == 2 ? $"The Big Bet has to be at least half your money: ${min}." : $"The minimum bet is ${min}.");
        if (bet > _pot) return Result.Fail($"You only have ${_pot}.");

        var row = _bonusRows[_bonusRow];
        var current = row[^1];
        var next = DrawBonus();
        row.Add(next);
        var right = higher ? next.Rank > current.Rank : next.Rank < current.Rank;
        _pot += right ? bet : -bet;
        var how = right ? $"The {next}! Right, you win ${bet}." : next.Rank == current.Rank ? $"The {next}, a tie, and a tie loses ${bet}." : $"The {next}. Wrong, you lose ${bet}.";

        if (_pot <= 0)
        {
            FinishBonus($"{how} That's all of it. You leave with nothing.");
            return Result.Success;
        }
        if (row.Count == 1 + BonusRowPlays[_bonusRow])
        {
            if (_bonusRow == 2)
            {
                FinishBonus($"{how} {_teamNames[_matchWinner]} wins ${_pot}!");
                return Result.Success;
            }
            // The last card played moves up to start the next row, and the row's money is added.
            _bonusRow++;
            _pot += BonusRowPrizes[_bonusRow];
            _bonusRows.Add([next]);
            _phase = CardPhase.BonusOffer;
            _message = $"{how} Row cleared! ${BonusRowPrizes[_bonusRow]} added, you have ${_pot}. " + (_bonusRow == 2
                ? $"Now the Big Bet: at least ${MinBetNow()}. Your card is the {next}. Keep it or change it?"
                : $"Your card is the {next}. Keep it or change it?");
            return Result.Success;
        }
        _message = $"{how} You have ${_pot}. Bet and call higher or lower than the {next}.";
        return Result.Success;
    });

    private void EnterBetting()
    {
        _phase = CardPhase.BonusBet;
        var card = _bonusRows[_bonusRow][^1];
        _message = _bonusRow == 2
            ? $"The Big Bet! At least ${MinBetNow()} of your ${_pot}. Higher or lower than the {card}?"
            : $"You have ${_pot}. Bet at least ${MinBetNow()}, then call higher or lower than the {card}.";
    }

    private void FinishBonus(string message)
    {
        _winnings = Math.Max(_pot, 0);
        _pot = _winnings;
        _phase = CardPhase.BonusOver;
        _message = message;
    }

    private Card DrawBonus()
    {
        if (_bonusDeck.Count == 0) _bonusDeck = new Stack<Card>(Card.FullDeck().OrderBy(_ => _rng.Next()));
        return _bonusDeck.Pop();
    }

    // ---- Seats and snapshots -------------------------------------------------------------

    public Seat GetSeat(Guid token)
    {
        lock (_gate) return _emceeToken == token ? new Seat(Role.Emcee) : new Seat(Role.Spectator);
    }

    public CardSnapshot Snapshot()
    {
        lock (_gate)
        {
            var rows = Enumerable.Range(0, Teams)
                .Select(t => new RowInfo(_teamNames[t], _gamesWon[t], _rowLength, _rows[t].Cards.ToList(), _rows[t].Frozen, _rows[t].Failed))
                .ToList();
            return new CardSnapshot(
                Code: Code,
                Phase: _phase,
                EmceeName: _emceeName,
                Rows: rows,
                GameNumber: _gameNumber,
                QuestionNumber: _question,
                SuddenDeath: IsSuddenDeath,
                Label: Label,
                QuestionText: _questionText,
                Answer: _answer,
                AnswerShown: _answerShown,
                Guess: _guess,
                Guesser: _guesser,
                CalledHigher: _calledHigher,
                Control: _control,
                Turn: _phase is CardPhase.Offer or CardPhase.Play ? _turn : -1,
                CanFreeze: _phase == CardPhase.Play && !IsSuddenDeath && _progress >= 1,
                GameWinner: _gameWinner,
                MatchWinner: _matchWinner,
                Message: _message,
                CanStart: _phase == CardPhase.Lobby && _emceeToken is not null,
                Bonus: _phase is CardPhase.BonusOffer or CardPhase.BonusBet or CardPhase.BonusOver
                    ? new BonusInfo(_pot, _bonusRow, _bonusRows.Select(r => (IReadOnlyList<Card>)r.ToList()).ToList(),
                        BonusRowPlays.Select(p => p + 1).ToList(), _phase == CardPhase.BonusOver ? 0 : MinBetNow(), _winnings)
                    : null);
        }
    }

    // ---- Internals -----------------------------------------------------------------------

    private bool IsSuddenDeath => _question >= QuestionsPerGame && _phase is not (CardPhase.Lobby or CardPhase.GameOver or CardPhase.MatchOver or CardPhase.BonusOffer or CardPhase.BonusBet or CardPhase.BonusOver);

    private string Label => _phase switch
    {
        CardPhase.Lobby => "Lobby",
        CardPhase.MatchOver => "Match over",
        CardPhase.BonusOffer or CardPhase.BonusBet or CardPhase.BonusOver => "Money Cards",
        _ => (_gameNumber == 3 ? "Tiebreaker game" : $"Game {_gameNumber}") + (IsSuddenDeath ? " · Sudden death" : $" · Question {_question} of {QuestionsPerGame}")
    };

    private void BeginMatch()
    {
        Array.Clear(_gamesWon);
        _gameNumber = 1;
        _matchWinner = -1;
        _gameWinner = -1;
        _questionDeck = [];
        _questionPos = 0;
        BeginGame();
    }

    private void BeginGame()
    {
        _rowLength = _gameNumber == 3 ? TiebreakRow : FullRow;
        _question = 0;
        for (var t = 0; t < Teams; t++)
        {
            _rows[t].Cards.Clear();
            _rows[t].Frozen = -1;
            _rows[t].Failed = false;
            _decks[t] = new Stack<Card>(NewDeck(t));
        }
        NextQuestion();
    }

    private IEnumerable<Card> NewDeck(int team)
    {
        // The stack is filled so that the first card listed comes off the top first.
        var order = _deckFactory?.Invoke(team) ?? Card.FullDeck().OrderBy(_ => _rng.Next()).ToList();
        return order.Reverse();
    }

    private Card Draw(int team)
    {
        if (_decks[team].Count == 0) _decks[team] = new Stack<Card>(Card.FullDeck().OrderBy(_ => _rng.Next()));
        return _decks[team].Pop();
    }

    private void NextQuestion()
    {
        _question++;
        // The team that guesses alternates every question, and the first guesser alternates every game: A, then B, then A.
        _guesser = (_gameNumber - 1 + _question - 1) % 2;
        if (_questionPos >= _questionDeck.Count)
        {
            _questionDeck = _content.CreateQuestions(_rng, 20).ToList();
            _questionPos = 0;
        }
        var q = _questionDeck[_questionPos++];
        _questionText = q.Text;
        _answer = q.Answer;
        _answerShown = false;
        _guess = null;
        _calledHigher = null;
        _control = -1;
        _turn = -1;
        _phase = CardPhase.Guess;
        _message = IsSuddenDeath
            ? $"SUDDEN DEATH! {_teamNames[_guesser]}: guess a number from 0 to 100."
            : $"{_teamNames[_guesser]}: guess a number from 0 to 100.";
    }

    private void StartTurn(int team, bool isControl)
    {
        _turn = team;
        _turnIsControl = isControl;
        _progress = 0;
        var row = _rows[team];
        if (row.Cards.Count == 0)
        {
            row.Cards.Add(Draw(team));
            // Only a team that won control may swap its first card.
            if (isControl)
            {
                _phase = CardPhase.Offer;
                _message = $"{_teamNames[team]}: your first card is the {row.Cards[0]}. Keep it or change it?";
                return;
            }
        }
        _phase = CardPhase.Play;
        _message = $"{_teamNames[team]}: higher or lower than the {row.Cards[^1]}?";
    }

    private void WinGame(int team, string message)
    {
        _gamesWon[team]++;
        _gameWinner = team;
        _turn = -1;
        _phase = CardPhase.GameOver;
        _message = message;
    }

    private T Run<T>(Func<T> action) where T : IResult
    {
        T result;
        lock (_gate)
        {
            result = action();
        }
        if (result.Ok) Changed?.Invoke();
        return result;
    }

    private bool IsEmcee(Guid token) => _emceeToken is not null && _emceeToken == token;

    private static bool TryCleanText(string? text, int max, out string clean)
    {
        var chars = (text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray();
        clean = string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > max) clean = clean[..max].Trim();
        return clean.Length > 0;
    }
}
