# BuzzerGames

Making our favorite game shows playable, on phones, with a big screen in the middle of the room.

Right now there is one game, **Jeopardy**. The site opens on a "pick a game" page, with Wheel of Fortune,
Classic Concentration, Super Password, and The Price Is Right waiting in the wings.

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

**Editing the board.** `src/BuzzerGames.Core/Data/PressYourLuck/board-1.json` and `board-2.json` hold the square counts, the cash values and the prize list for each round. Round 1 leans on the newer show (lots of `+ SPIN`), round 2 on the older one (more prizes). Whammies per face (2, 3 and 4) are set there too. The values are my guesses, not the real 80s table.

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

src/BuzzerGames.Web/           The website
  Program.cs                     startup and wiring
  Games/GameCatalog.cs           the "pick a game" list
  Components/Pages/              Home, JeopardyHome, JeopardyRoom (the one page every screen goes through)
  Components/Jeopardy/           LobbyView, BoardView (TV), EmceeView, PlayerView, ScoreStrip
  Components/PressYourLuck/      the Press Your Luck screens (TV, emcee, player, lobby)
  wwwroot/app.css, js/bg.js      styling, and the small JavaScript bits (saved seat, QR code)
  wwwroot/js/pyl.js              the flashing Big Board and its sounds

tests/                         Checks that play real games and verify the rules and the screens
Dockerfile                     How Railway builds and runs it
```

## What's next

- Swap the dummy questions for a real dataset (the engine only knows about `IGameDataSource`).
- Postgres, to remember which games have been played so none repeat.
- The next game show.
