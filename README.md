# BuzzerGames

Making our favorite game shows playable, on phones, with a big screen in the middle of the room.

Right now there are three games: **Jeopardy**, **Press Your Luck** and **Password Plus**. The site opens on a
"pick a game" page, with Wheel of Fortune, Classic Concentration and The Price Is Right waiting in the wings.

## How a Jeopardy game works

- One **emcee** and three **players**. Everyone joins from the same web address.
- The **big screen** (a TV or projector) opens the site and taps **Create a new game**. That lands on
  `/jeopardy/ABCD/board`, which shows a big QR code while people join and becomes the game board when the game starts.
  The big screen never takes a seat, and it never shows an answer, so it is safe to put on a TV.
- Phones scan the QR code (or type the four-letter room code). That opens `/jeopardy/ABCD`, which offers
  **Join as Emcee** and **Join as Player**. Once the table is full, **Start Game** appears.
- Phones deliberately do **not** show the clue text. Everyone reads the clue off the big screen.
- Contestants call out a category and amount. The **emcee taps that clue** on their phone, and the clue appears on the big screen.
- Buzzers are open the whole time the clue is showing. **First buzz wins.**
- The emcee sees the correct answer and taps **Correct** or **Wrong**.
  Wrong costs points and the clue goes live again for everyone else.
- If nobody knows it, the emcee taps **reveal the answer**. Whoever picked last keeps control.
- Whoever answered last correctly picks the next clue.
- Two rounds, Daily Doubles (1 in round one, 2 in round two), and Final Jeopardy with wagers and typed answers.

## How a Press Your Luck game works

Same setup as Jeopardy: create it on the TV at `/press-your-luck`, one emcee and three players join by QR code.

- **Question round.** The TV asks an open question and the first player to buzz gives a guess out loud. The emcee taps right or wrong (and can type what they said).
  - Right: two bogus answers join it and the guesser earns **3 spins**.
  - Wrong: the real answer plus one bogus answer are shown.
  - The other two players pick one of the three choices on their phones. A correct pick earns **1 spin**. Wrong picks cost nothing.
- **Big Board.** 18 squares, three faces each, so the light really lands on one of 54 outcomes. Fewest spins goes first. A player taps **SPIN**, then **PRESS!** to stop the light. Cash, cash plus a spin, prizes, Pick a Corner, or a **Whammy** that wipes out their score. Four Whammies and they are out. Spins can be passed to the highest-scoring opponent after at least one spin.
- Two rounds; the second round pays more.
- The light is decided by the server, so a laggy phone can't cheat. `PressLagMs` and `TickMs` in `PressYourLuckGame.cs` control the lag allowance and speed.

**Sound.** The TV has a "Click to turn on sound" button (browsers require a click). It plays built-in beeps. To use your own, drop files in `src/BuzzerGames.Web/wwwroot/sounds/` named `spin` (loops while the light runs and cuts off the moment someone presses) and `whammy` (plays on a Whammy), as `.mp3`, `.wav` or `.ogg`. Anything missing falls back to a built-in beep, and cash and prize results are silent because the host reads the amount.

**Pictures.** Put these in `src/BuzzerGames.Web/wwwroot/images/`: `whammy1.png` to `whammy6.png` (square; every Whammy square on a board gets one picked at random, and the one you hit is blown up in the middle) and `press_your_luck.png` (the logo in the middle, 4 squares wide by 3 tall, shown whenever there is no result). If a picture is missing the game draws a stand-in, so nothing breaks.

**Big Bucks.** One square per board. When the light lands on it, it pays the best cash amount showing on the board at that moment, and that square lights up too.

**Move One Space.** Two squares per board. Landing on one lets the player choose the square on either side of it, seeing what each shows, and take that. It never sits on a corner or next to Pick a Corner or another Move One Space.

**Colors and lights.** Cash squares are dealt one of eight colors (`.c0` to `.c7` in `app.css`), Big Bucks is green, and the square the light is on gets a ring of blinking bulbs.

**Questions.** Press Your Luck has its own bank, `src/BuzzerGames.Core/Data/PressYourLuck/questions.json`: general-knowledge questions with one short spoken answer and two hand-picked wrong answers. Each game deals six, all from different categories. `level: 2` marks a harder one (round 2 favors those), and `holiday: true` marks the ones only used when the holiday box is ticked. It does not use the Jeopardy bank.

**Editing the board.** `src/BuzzerGames.Core/Data/PressYourLuck/board-1.json` and `board-2.json` hold the square counts, the cash values and the prize list for each round. Round 1 leans on the newer show (lots of `+ SPIN`), round 2 on the older one (more prizes). Whammies per face (2, 3 and 4) are set there too. The values are my guesses, not the real 80s table.

## How a Password Plus game works

Five people: **Emcee**, **Team A** (player 1, player 2) and **Team B** (player 1, player 2). Everyone joins from a phone; the TV
is the `/board` address.

**A puzzle (a round)** has a secret "big word" (always a person, place or thing) and five password words that hint at it, played one after another, getting easier: word 1 is true but almost useless, word 5 makes it obvious. The game is **spoken**: clues and guesses are said out loud, nothing is typed. The TV shows only the puzzle board, modelled on the show's Password Puzzle: the neon sign on top, five stacked word slots below. A word appears in its slot only once it is guessed or posted, and when the puzzle is solved the big word replaces the sign.

**Roles swap per puzzle, not per word.** In puzzle 1 player 1 on each team gives clues and player 2 guesses; in puzzle 2 they swap; and so on.

**A word.** The clue-giver's phone shows the password; the guesser's phone is idle (guessers never see it). The team on the clock gives ONE spoken word and their guesser says a guess. The emcee rules with three buttons: green ✓ (the guesser got the word), red ✗ (wrong) and yellow ! (invalid clue: more than one word, part of the password...). Wrong or invalid, and the turn passes to the other team; an invalid clue costs only that clue. Each side gets 4 clues, 8 in all. If nobody gets the word it goes up on the board, the other team gives the first clue on the next word, and the puzzle carries on (nobody gets a shot at the big word from that one).

**Control.** A coin flip decides who starts the first word. The team that last solved a puzzle chooses **Play** (they give the first clue) or **Pass** at the first word of the next puzzle.

**The big word.** When the emcee marks a word right, just that word is revealed and only that guesser may try the big word; the emcee rules it right or wrong. Right: the puzzle is over, it scores, and everything is revealed. Wrong: control goes to the other team, who give their first clue on the next word. The exception is word 5: the guesser, then their clue-giver, then the other team's guesser, then their clue-giver. If nobody gets word 5, the last team to guess a word in this puzzle starts that same chain. If nobody guessed a single word, the puzzle is thrown out.

**Scoring.** Puzzles are worth 100, 100, then 200 each. The first team to **500** goes to the bonus round. Only one team scores per puzzle, so there are never ties, and the game just deals more puzzles if it needs them. Scores are on the phones, not the TV board.

**The bonus round.** The winning team gets ten single words in alphabetical order, one per letter, starting at a random letter and wrapping from Z back to A (so Q to Z, or X to G). It runs on the TV with a 60-second clock, the ten letters typed out (A B C D…), and the current word in giant type. One partner faces the TV and gives clues out loud; the other sits with their back to it and guesses. Nobody uses their phone: **only the emcee rules**, with three buttons: a green ✓ (got it), a yellow PASS, and a red ✗ (a clue rule was broken, so the word is zapped). A passed word goes to the back of the line and comes up again after the J word if there is still time; a zapped word is lost for good. $100 per word, **$10,000 for all ten**. The clock is the server's, so the round ends by itself when time is up. Before starting, the emcee can swap who gives the clues (player 1 does by default).

**Bonus words.** `src/BuzzerGames.Core/Data/Password/bonus-words.txt`: one word per line, in any order (the letter is just the first letter), `#` for comments. Single words only: common things, places, or a first or last name of someone famous ("Clinton" yes, "Anderson" too vague). Just paste more words in and rebuild; the checks make sure every letter has enough. Words that were dealt recently are skipped, so games on the same night don't repeat until a letter runs out of fresh ones.

**Playing again.** After the bonus round the emcee chooses: *same players* (everyone stays signed in), *switch partners* (same phones, no QR code; the two second players trade teams, so A1 + B2 play B1 + A2), or *new players* (back to the lobby, everyone scans again).

**Emcee tools.** The emcee's phone shows the password, the big word and the other answers to accept, and has the ✓ / ✗ / ! buttons and Correct / Wrong for the big word. "Swap this puzzle" (two taps, to be safe) throws out a dud puzzle and starts the next one at word 1, with scores and control untouched.

**What makes a good password.** The clue-giver only ever sees the password in play, never the big word, so a clue that leaks the big word is legal (that's part of the fun). The one thing that matters is that a password is easy to *clue*: concrete and vivid with an obvious association. Abstract words (think, open, blow) have no single hook and make a miserable password. (The bonus round is different: the guesser knows the letter, so abstract words work there.)

**Puzzles.** `src/BuzzerGames.Core/Data/Password/puzzles.json`: each entry has a `kind` (Person, Place or Thing), the `answer`, five `words` ordered from vague to obvious, and optional `alsoAccept`. Each game deals 12, avoiding the same kind twice in a row.

## Run it on your computer

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/BuzzerGames.Web
```

Then open <http://localhost:5080>. To try the whole thing by yourself you need **five windows**:

1. Window 1 is the TV: click Create a new game on `/jeopardy`. Leave it on its `/board` address.
2. Windows 2 to 5 are the phones: open `/jeopardy/ABCD` (use the room code from the TV) and join as the emcee and
   as three players. Each browser remembers its own seat, so use one normal window plus private/incognito windows
   (or different browsers). Resize them small to see what a phone gets.

## Run the checks

```bash
dotnet run --project tests/BuzzerGames.Core.Checks   # the rules of the game, no browser needed
dotnet run --project tests/BuzzerGames.Web.Checks    # draws every screen and checks what it shows
```

Both print a pass/fail line per check and exit with a non-zero code if anything fails.

## Deploy to Railway

1. In Railway: **New Project → Deploy from GitHub repo**, and pick this repo. Railway finds the `Dockerfile` by itself.
2. Open the service's **Settings → Networking → Generate Domain** to get a public address.
3. Optional: set the health check path to `/healthz`.
4. Keep it at **one replica**. Games live in the server's memory, so a second copy of the server wouldn't know about
   the first copy's games.

Things to know:

- A redeploy or restart wipes any game in progress. Don't push changes during the party.
- Phones keep their seat in the browser, so locking the screen or refreshing doesn't lose it.
- The QR code is drawn in the browser from the page's own address, so it just works on whatever domain you end up with.

## Where things live

```
src/BuzzerGames.Core/          The game itself: plain C#, no web code
  Jeopardy/JeopardyGame.cs       every rule: lobby, buzzing, judging, Daily Doubles, Final Jeopardy
  Jeopardy/GameData.cs           the shape of a game's questions
  Jeopardy/IGameDataSource.cs    "where do questions come from?" (swap this to change them)
  Jeopardy/DummyGameDataSource.cs  placeholder questions for testing
  Jeopardy/Snapshots.cs          frozen pictures of the game that pages draw from
  Jeopardy/GameRoomManager.cs    all running games, found by room code
  PressYourLuck/                 the Press Your Luck engine, board dealing, questions, room manager
  Data/PressYourLuck/            board recipes (counts, values, prizes)
  Password/                      the Password Plus engine, puzzle dealing, room manager
  Data/Password/                 the puzzle bank (puzzles.json) and the bonus words (bonus-words.txt)

src/BuzzerGames.Web/           The website
  Program.cs                     startup and wiring
  Games/GameCatalog.cs           the "pick a game" list
  Components/Pages/              Home, JeopardyHome, JeopardyRoom (the one page every screen goes through)
  Components/Jeopardy/           LobbyView, BoardView (TV), EmceeView, PlayerView, ScoreStrip
  Components/PressYourLuck/      the Press Your Luck screens (TV, emcee, player, lobby)
  Components/Password/           the Password Plus screens (TV, emcee, player, lobby)
  wwwroot/app.css, js/bg.js      styling, and the small JavaScript bits (saved seat, QR code)
  wwwroot/js/pyl.js              the flashing Big Board and its sounds

tests/                         Checks that play real games and verify the rules and the screens
Dockerfile                     How Railway builds and runs it
```

## What's next

- Swap the dummy questions for a real dataset (the engine only knows about `IGameDataSource`).
- Postgres, to remember which games have been played so none repeat.
- The next game show.
