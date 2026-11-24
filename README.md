# BuzzerGames

Making our favorite game shows playable, on phones, with a big screen in the middle of the room.

Right now there are nine games: **Jeopardy**, **Press Your Luck**, **Password Plus**, **Match Game**, **Card Sharks**, **The Floor**, **Wheel of Fortune**, **$100,000 Pyramid** and **Family Feud**. The site opens on a
"pick a game" page.

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

**A puzzle (a round)** has a secret "big word" (always a person, place or thing) and five password words that hint at it, played one after another, getting easier: word 1 is true but almost useless, word 5 makes it obvious. The game is **spoken**: clues and guesses are said out loud, nothing is typed. The TV shows the puzzle board, modelled on the show's Password Puzzle (the neon sign on top, five stacked word slots below), with the two team scores underneath. A word appears in its slot only once it is guessed or posted, and when the puzzle is solved the big word replaces the sign.

**Roles swap per puzzle, not per word.** In puzzle 1 player 1 on each team gives clues and player 2 guesses; in puzzle 2 they swap; and so on.

**A word.** The clue-giver's phone shows the password; the guesser's phone is idle (guessers never see it). The team on the clock gives ONE spoken word and their guesser says a guess. The emcee rules with three buttons: green ✓ (the guesser got the word), red ✗ (wrong) and yellow ! (invalid clue: more than one word, part of the password...). Wrong or invalid, and the turn passes to the other team; an invalid clue costs only that clue. Each side gets 4 clues, 8 in all. If nobody gets the word it goes up on the board, control stays where it was, and the puzzle carries on (nobody gets a shot at the big word from that one).

**Control.** A coin flip decides who has control for the first word. At the start of *every* word both clue-givers see the new password, and the clue-giver on the team with control decides **Play** (their team gives the first clue) or **Pass** (the other team does), based on how clueable the word looks, so there is real strategy in being the second team to clue. Whichever team gets a word right takes control (or keeps it), even if they then miss the big word. When a team **solves the big word** and scores, control goes to the *other* team for the next puzzle, so one team can't run away with the game. If nobody gets a word, control doesn't move.

**The big word.** When the emcee marks a word right, just that word is revealed and only that guesser may try the big word; the emcee rules it right or wrong. Right: the puzzle is over, it scores, and everything is revealed. Wrong: the puzzle carries on to the next word, and that team keeps control. The exception is word 5: the guesser, then their clue-giver, then the other team's guesser, then their clue-giver. If nobody gets word 5, the last team to guess a word in this puzzle starts that same chain. If nobody guessed a single word, the puzzle is thrown out.

**Scoring.** Puzzles are worth 100, 100, then 200 each. The first team to **500** goes to the bonus round. Only one team scores per puzzle, so there are never ties, and the game just deals more puzzles if it needs them. 

**The bonus round.** The winning team gets ten single words in alphabetical order, one per letter, starting at a random letter and wrapping from Z back to A (so Q to Z, or X to G). It runs on the TV with a 60-second clock, the ten letters typed out (A B C D…), and the current word in giant type. One partner faces the TV and gives clues out loud; the other sits with their back to it and guesses. Nobody uses their phone: **only the emcee rules**, with three buttons: a green ✓ (got it), a yellow PASS, and a red ✗ (a clue rule was broken, so the word is zapped). A passed word goes to the back of the line and comes up again after the J word if there is still time; a zapped word is lost for good. $100 per word, **$10,000 for all ten**. The clock is the server's, so the round ends by itself when time is up. Before starting, the emcee can swap who gives the clues (player 1 does by default).

**Bonus words.** `src/BuzzerGames.Core/Data/Password/bonus-words.txt`: one word per line, in any order (the letter is just the first letter), `#` for comments. Single words only: common things, places, or a first or last name of someone famous ("Clinton" yes, "Anderson" too vague). Just paste more words in and rebuild; the checks make sure every letter has enough. Words that were dealt recently are skipped, so games on the same night don't repeat until a letter runs out of fresh ones.

**Playing again.** After the bonus round the emcee chooses: *same players* (everyone stays signed in), *switch partners* (same phones, no QR code; the two second players trade teams, so A1 + B2 play B1 + A2), or *new players* (back to the lobby, everyone scans again).

**Emcee tools.** The emcee's phone shows the password, the big word and the other answers to accept, and has the ✓ / ✗ / ! buttons and Correct / Wrong for the big word. "Swap this puzzle" (two taps, to be safe) throws out a dud puzzle and starts the next one at word 1, with scores and control untouched.

**What makes a good password.** The clue-giver only ever sees the password in play, never the big word, so a clue that leaks the big word is legal (that's part of the fun). The one thing that matters is that a password is easy to *clue*: concrete and vivid with an obvious association. Abstract words (think, open, blow) have no single hook and make a miserable password. (The bonus round is different: the guesser knows the letter, so abstract words work there.)

**Puzzles.** `src/BuzzerGames.Core/Data/Password/puzzles.json`: each entry has a `kind` (Person, Place or Thing), the `answer`, five `words` ordered from vague to obvious, and optional `alsoAccept`. Each game deals 12, avoiding the same kind twice in a row.

## How a Match Game works

An **emcee**, a panel of **celebrities** (two or more, no upper limit) and two contestant **teams, A and B**. The teams never log in: they
just know which team they are on and speak their answers out loud. Only the emcee and the celebrities use phones.

1. **Each team gets three questions**, alternating A, B, A, B, A, B. The emcee reads a fill-in-the-blank question about a family member.
   Every celebrity in play types a secret answer on their phone, then the emcee closes the answers. The team says its guess out loud
   (the emcee may type it in to show on the TV).
2. **The reveal.** The celebrities reveal in seat order, each with their own button, so they can explain their answer or get excited. The emcee
   rules **match** or **no match** on each one, and the TV stamps the result. Each match is a point for the team that is up.
3. **A celebrity who matches sits out that team's remaining questions.** Each team tracks its own list, so if Team A matches celebrities 1, 2 and 3
   its next question uses only 4, 5 and 6, while Team B still has everyone. Only the celebrities in a question answer and reveal. A team with
   nobody left is skipped.
4. **Ties** after the six questions are settled by one tiebreaker question per team with every celebrity back in play, repeated until someone leads.
5. **The Super Match** goes to the team with more matches. Each blank has three fixed popular answers worth **$500, $250 and $100**
   ("Super ___": Mario, Man, Market). Part one is all spoken: the emcee picks three celebrities as advisors, they advise the contestant out loud
   (they may all be garbage), and the contestant says an answer. The emcee records which popular answer it matched, if any, and only that answer
   sets the stake, or a consolation **$50** if it isn't on the board. Then the emcee reveals the board slowly, $100 first, then $250, then $500.
   The matched row lights up, and all three are shown. Part two uses the phones again: the contestant plays for **ten times** the stake and picks one celebrity,
   who secretly types an answer. The contestant says theirs out loud, the celebrity does the reveal on their own phone, and the emcee judges an exact match.

Celebrities who join after the start play from the next question. After the game the emcee can play again with the same panel.

**Question bank:** `src/BuzzerGames.Core/Data/Match/match-game.json`. `names` are the family first names that fill `{name}` in each of the `questions`.
Each of the `supers` has a blank (`text`) and `answers`: three lists, most popular first, each holding the accepted spellings. The computer only
*suggests* which row an answer matches, by spelling; the emcee always decides. The Super Match blanks and rankings are my own modernized estimates, informed by the show's real ranked answers (see Credits). Replace any you disagree with.

## How a Card Sharks game works

One emcee runs everything from a phone; the two teams just play out loud. The TV shows the survey question and both rows of cards.

1. The emcee reads a survey question ("We asked 100 married men..."); the answer is a number from 0 to 100, visible only on the emcee's phone.
2. The guessing team says a number and the emcee types it in. The other team calls higher or lower. A right call wins control; a wrong call (or an exact hit) leaves control with the guesser. The guesser alternates each question.
3. The team in control gets its base card turned over and may keep it or swap it once for the top card of its own 52-card deck (final). Then it calls higher or lower on each next card. A tie is a miss. Aces are high.
4. After at least one right call, the team may freeze: its progress is locked and the turn ends. A miss removes every card turned since the last freeze, and the other team plays its row (base card turned, no swap, it may freeze and may win).
5. Completing the row (7 cards in games one and two, 4 in the tiebreaker) wins the game. Four questions per game; the fourth is sudden death: the control winner decides who finishes, no freezing, finish or lose.
6. First to two games wins the match.

**Money Cards (the bonus round).** The match winners start with $200 and one 52-card deck. Bet at least $50 and call higher or lower on each card (a tie loses the bet). The bottom row is the base card plus 3 more; clearing it moves the last card up to start the $400 row (+$400), and clearing that moves its last card up to the Big Bet row (+$1,000), where one bet of at least half the money decides the prize. The base card of each row can be kept or changed once. Lose everything on a bet and the card that sank you moves up to the next row with that row's money ($400, then $1,000), so one bad bet doesn't end it. Only losing everything on the Big Bet leaves $0.

Survey questions live in `src/BuzzerGames.Core/Data/CardSharks/card-sharks.json`. The ones in there now are **placeholders**; swap in your family's real results.

## How a Floor game works

Create it on the TV at `/the-floor`. Everyone scans the QR code; one person joins as the **emcee**, everyone else joins as a **player**.

- **Pick an expertise.** Each player picks one topic (Dog Breeds, Acronyms, Movie Posters...). A topic can be held by one player at a time.
- **Build the floor.** The emcee taps **Build the floor** (4 to 20 players). Each player gets one square, packed into as close to a block as possible
  (a ragged top row is fine). Each territory shows the player's name and **all** their topics.
- **First challenge.** The first challenger is chosen completely at random. A challenger may only challenge a **neighbor** (a square sharing an edge).
- **The duel.** Both players are quizzed on the **defender's** topics. The challenger goes first. Each player has their own clock (45 seconds by default);
  only one runs at a time. The picture is on the TV, the answer is on the emcee's phone only. The emcee taps **Correct** (the clock passes to the other
  player and a new picture comes up) or **Pass** (the answer shows to the room for 3 seconds with the clock still running, then a new picture).
  Run out of time and the whole TV goes red. Undo takes back the last Correct or Pass, and Pause stops the clocks.
- **The takeover.** The winner takes the loser's territory, and (unless the emcee switched **Winner inherits topics** off) the loser's topics too.
  A defender with several topics is asked a mix, their own topic a little more often. The winner challenges next, or can **pass**:
  another living player is then picked at random and has to challenge (a turn that was passed to you can't be passed on).
- **The final duel.** With two players left there is no choosing and no sides. Who starts is random, and every topic used anywhere in the game is in play.
- **Running out of pictures.** A picture is never shown twice in a game. If a defender's topics have run low they must pick a new topic before they can be
  challenged (or the emcee assigns a random one). If a duel runs out of pictures mid-way, whoever has more time left wins.
- **Topics get used up.** A topic picked in one game can't be picked in the next until the emcee taps **Make all topics eligible again** in the lobby.

### Topics and pictures

Open `/the-floor/topics` (there is a link at the bottom of the Floor start page). Make a topic, then paste screen clips straight in
(**Win+Shift+S**, then **Ctrl+V**), type the answer, and press Enter. Or drop in many files at once; the answer is guessed from the file name.
Pictures are shrunk to about 900 pixels in your browser before they are uploaded.

- A picture's **file name is its answer**: `golden-retriever.jpg` is shown to the emcee as "Golden Retriever". `_2` or ` (2)` on the end is ignored.
  Anything a file name can't spell (a colon, say) is kept in an `answers.txt` beside the pictures.
- A topic can also hold **text prompts** (`prompts.txt`, one per line: `clue | answer | other accepted answers separated by ;`).
- A topic needs at least 20 pictures before players can pick it (`FLOOR_MIN_PROMPTS` changes this). A duel uses roughly 35 to 50 pictures,
  and a topic that keeps winning gets quizzed again, so **aim for 100 or more per topic**. The topics page shows a rough "duels" estimate.
- Three small text topics ship inside the program (Acronyms, Finish the Book Title, Name the Movie from the Quote) so there is something to play on day one.

### Sound on The Floor

Every sound is generated in the browser (Web Audio), so there are no audio files to host. Only the TV plays sound. Chrome blocks audio until someone interacts with the page, so tap **Tap for sound** in the TV's top bar once (the first click or key press on the TV also turns it on). The choice is remembered. Hear every effect without a game at `/the-floor/sounds`.

### Railway setup for The Floor

Pictures and the list of used-up topics live in a folder, so on Railway they need a **Volume**:

1. Service → **Volumes** → add a volume mounted at `/data`.
2. Service → **Variables**: `FLOOR_DATA_DIR` = `/data`, and `FLOOR_ADMIN_PASSCODE` = a passcode of your choosing (the topics page is locked without it).
3. Redeploys keep everything in `/data`. Without the volume the pictures vanish on every deploy.

On your own computer neither variable is needed: pictures go in a `floor-data` folder (ignored by git) and the topics page opens without a passcode.

## How a $100,000 Pyramid game works

- Only an **emcee** (one phone) and a **TV**. Create the game on the TV (`/pyramid`); it shows a QR code. The emcee scans it, taps **I'm the emcee** (no name, no password) and **Start Game**. The emcee organizes two teams offline ("Team A" and "Team B").
- A **game** is six topics in a pyramid, three per team, 7 words each (21 is perfect). The emcee reveals the topics one at a time, the team up picks one, and the emcee taps it. The clue giver faces the TV, the guesser doesn't. The emcee reads the description, taps **Start the clock**, and the TV shows the clock, the current word and the team's score. The emcee marks each word ✓, ✗ (dead) or **Pass** (goes to the back). A round is 30 seconds for 7 words, 1 point each.
- The team with the lower score picks first each round; a tie keeps the order. One topic per game is a **Mystery 7**: the team only gets the title, and the emcee reveals what the words had in common afterward (7 of 7 wins a special prize).
- A tie after six topics goes to the **tiebreaker**: a coin flip decides who picks one of two letters, the other team gets the other, and each gets seven words starting with it. Repeat until someone wins.
- The winner plays the **bonus pyramid**: 60 seconds, six subjects ("Things a toaster might say"), the emcee marks **Valid** (the square turns to dollars), **Invalid** (gone for good) or **Pass** (tap a passed square to come back). $25,000 for all six (the squares alone add up to $12,000), or $100,000 (squares add up to $40,000) when the same team reaches the pyramid twice. The emcee can override the prize.
- A **match** is two games; between them the emcee swaps the celebrities. Topics are managed at `/pyramid/topics` (same passcode as The Floor and Wheel).

## How a Family Feud game works

- Only an **emcee** (one phone) and a **TV** (a laptop on the big screen). Create the game on the TV (`/family-feud`); it shows a QR code. The emcee scans it, taps **I'm the emcee**, names the two families and taps **Start Game**. Nobody else signs in.
- **Buzzers are the mouse**: for the face-off, one family's player clicks the **left** button on the laptop and the other the **right** button (the right-click menu is suppressed). First click wins the buzz.
- Each round: the emcee reads the question, the families face off, and the emcee marks the first answer. The better answer chooses to **play or pass**. The family in control keeps going: the emcee taps the answer on his phone when it's on the board (the panel slides left to show the answer and its points) or **Strike** when it isn't. Three strikes and the other family gets one **steal** answer. Whoever wins the round banks the points.
- Rounds 1 and 2 are worth face value, round 3 is **double**, round 4 onward **triple**, until a family reaches **300**. The game picks the boards to match: rounds 1 to 3 get longer boards (five to eight answers), and the triple rounds get four-answer boards, the winner-takes-all finish. Skipping a question gets you another of the same kind, and a bank short of four-answer boards just uses whatever it has. Points are banked when the emcee taps **Continue**.
- **Fast Money**: one player from the winning family answers five questions in 20 seconds, then a second player gets 25 seconds on the same questions (repeating an answer is rejected). The emcee types what was said or taps a board answer. Then the answers are revealed one by one, text first, then points, and the total is checked against **200**.
- **The question is never on the TV.** The emcee reads it aloud (and repeats it for each contestant); it is only on his phone, along with every answer. The TV shows just the round, the board, the strikes and the scores.
- A board holds three to eight answers. Questions live on `/family-feud/questions` (behind the same passcode as the other admin pages): add one at a time or paste lines like `Name something you bring to the beach | Towel: 38 | Sunscreen: 25 | Umbrella: 15`.
- The bank ships with about 6,500 **real Family Feud boards** (real survey counts) from the ProtoQA dataset, filtered for a family crowd: nothing with drinking, drugs, sex, violence or crude language, and at least four answers. They're bundled as `Data/Feud/feud-starter.json` and loaded the first time the game runs. Redeploying with a newer starter set swaps out the older set's questions and leaves any you added yourself. To rebuild the bundle, or make a smaller file to paste into the questions page: `git clone --depth 1 https://github.com/iesl/protoqa-data`, then `python tools/import_protoqa.py protoqa-data --json src/BuzzerGames.Core/Data/Feud/feud-starter.json` (or `--text feud-import.txt -n 300`).
- Question data: ProtoQA (Boratko et al., 2020), licensed CC BY 4.0, https://github.com/iesl/protoqa-data.
- The TV never contains an unrevealed answer in its page; only the emcee's phone has the full list.

## How a Wheel of Fortune game works

Create it on the TV at `/wheel-of-fortune`. The emcee and three players scan the QR code: the players take Red, Yellow and Blue in the order they join, and the emcee taps **Start Game**. Six regular puzzles are played, with a Triple Toss-Up set after puzzles 2 and 4 and the Bonus Round at the end (any of those is skipped if the puzzle store has none of it).

- **The TV** shows only the puzzle, the category and everyone's money (the letters used, whose turn it is and what happened are on the phones). When someone spins, the wheel fills the screen in a tilted view, then swings in close on that player's clicker. Matching tiles turn blue one at a time with a ding each, then turn into the letters one at a time. The wheel has a peg every 5 degrees, so every wedge clicks three times. Tap **Tap for sound** once on the TV (the first click also does it).
- **Players' phones** have the category, the letters used so far, and on their turn a spin pad: put a finger on the circle, swirl it and let go. How fast the finger was moving as it let go sets the spin (nothing on screen shows the strength, and one particular speed brings the wheel back to the same wedge). The speed range is set by `MIN_SPEED` and `MAX_SPEED` at the top of `wwwroot/js/wheelpad.js`. Everything else is said out loud.
- **The emcee's phone** shows the secret answer. They tap the letter the player says, see **Confirm K**, then confirm (so a typo can't cost anyone a turn), buy a vowel for a player, mark a solve **Correct** or **Incorrect**, skip a puzzle, and **Undo** any step.
- If the TV page is closed, the emcee's phone finishes the spin and the reveal itself a few seconds late, so the game never gets stuck.

**Rules as built.** Three players, six regular puzzles (puzzle n starts with player (n-1) % 3). The wheel has 24 wedges of 15 degrees: 21 cash wedges ($500 to $5,000), one Bankrupt, one Lose a Turn, and one wedge split into three 5-degree slices (Bankrupt, $1,000,000, Bankrupt). Landing on the Million slice picks up the wedge and pays $1,500 a letter; the wedge then becomes a plain 15-degree Bankrupt for the rest of the puzzle. Consonants pay the wedge value per letter, vowels cost $250 from the round bank (charged even on a miss), a wrong letter passes the turn, Bankrupt wipes the round bank (and a held Million wedge is lost for good). Only the puzzle solver banks the round money. The emcee types each letter, confirms it (typos happen), and judges solves. Undo is available all game.

**Triple Toss-Ups.** A set is three themed puzzles worth $1,000, $2,000 and $3,000. The TV shows the theme and the prize, the emcee taps **Start the toss-up**, and letters reveal one at a time (the TV drives the pace; the emcee also has **Show another letter** as a fallback). Anyone can hit **BUZZ** on their phone; the first one in is announced and the emcee judges. A wrong answer locks that player out for the rest of that puzzle. If every letter shows and nobody gets it, the puzzle ends. The winner is celebrated, and the money banks when the emcee continues.

**Bonus Round.** The leader plays (a tie is broken at random). They pick one of 24 face-down cards on their phone ($40,000 to $100,000; if they won the Million wedge, one random card is $1,000,000). The emcee taps one of three categories, RSTLNE are shown, and the player calls three consonants and a vowel. When the emcee taps **Your time starts now**, the TV counts down 10 seconds (`BonusSeconds` in `WheelRules.cs`). The clock is for show: the emcee judges the answer. The card turns over either way, only a correct answer pays, and the prize is added to the total when the emcee taps **Finish the game**.

**Spin.** Each player swirls a finger around a circle on their phone; the swirl's speed becomes a power from 0 to 1000. The wheel travels 0.5 to 1.75 turns, and a power of exactly 400 is the sweet spot (one full turn, back to the same place). The wheel stops with the clicker between two pegs, so a 15-degree wedge has three possible stopping places (the Million slice has one). The landing is worked out on the server before the wheel moves, so the animation can never disagree with the result.

**Puzzles.** Open `/wheel-of-fortune/puzzles` (same `FLOOR_ADMIN_PASSCODE` and data folder as The Floor, so there is no new Railway setup). Add puzzles one at a time with a live board preview, or paste many in the import box:

```
# regular
Phrase | THE EARLY BIRD CATCHES THE WORM
# quick
Things in a Kitchen | WOODEN SPOON | CAST IRON PAN | OVEN MITT
# bonus
Place | NIAGARA FALLS
```

Regular lines are `category | puzzle`, quick sets are `theme | puzzle 1 | puzzle 2 | puzzle 3`. Use `/` inside a puzzle to force a line break. Puzzles are stored in `wheel/puzzles.json` in the data folder; a handful of starters are added on first run. A puzzle must fit the 12/14/14/12 board.

**Getting lots of puzzles.** `tools/scrape_wheel_compendium.py` (Python 3, nothing to install) reads the Buy a Vowel Boards Puzzle Compendium and writes a `wheel-import.txt` in the importer format. Regular rounds (including prize puzzles) become regular puzzles, each Triple Toss-Up becomes a quick set, and the Bonus Round puzzles become bonus puzzles. It skips puzzles that can't fit the board, duplicates, alcohol/coffee puzzles and word-play bonus categories. Run it on your own computer (it pauses between requests and caches pages), then paste the file into the import box:

```
python tools/scrape_wheel_compendium.py --seasons 30-44
```

`--help` lists the options (`--regular`, `--quick`, `--bonus` cap how many are written; `--list-categories` shows the bonus categories).

## Credits

Match Game is owned by Fremantle; this is a private party game. The Super Match blanks draw on the transcribed episode data from
[matchgame.info](https://matchgame.info) ([rebeccadrucker/match-game-data](https://github.com/rebeccadrucker/match-game-data)), licensed CC BY 4.0.
The rankings in `match-game.json` were then modernized and edited by hand.

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
  Match/                         the Match Game engine, question dealing, room manager
  Floor/                         The Floor: engine (FloorGame), floor shape, topic folder store, room manager
  Data/Floor/                    the built-in text topics (acronyms, book titles, movie quotes)
  Data/Match/                    the question bank: names, questions, Super Match blanks (match-game.json)

src/BuzzerGames.Web/           The website
  Program.cs                     startup and wiring
  Games/GameCatalog.cs           the "pick a game" list
  Components/Pages/              Home, JeopardyHome, JeopardyRoom (the one page every screen goes through)
  Components/Jeopardy/           LobbyView, BoardView (TV), EmceeView, PlayerView, ScoreStrip
  Components/PressYourLuck/      the Press Your Luck screens (TV, emcee, player, lobby)
  Components/Password/           the Password Plus screens (TV, emcee, player, lobby)
  Components/Match/              the Match Game screens (TV desks and Super board, emcee, celebrity phone, lobby)
  wwwroot/app.css, js/bg.js      styling, and the small JavaScript bits (saved seat, QR code)
  wwwroot/js/pyl.js              the flashing Big Board and its sounds
  Wheel/                         Wheel of Fortune: engine, wheel layout and spin physics, board layout, puzzle store
  Data/Wheel/                    the starter puzzles (wheel-starter.json)
  Pyramid/                       $100,000 Pyramid: engine, topic store
  Data/Pyramid/                  the starter topics (pyramid-starter.json)
  Feud/                          Family Feud: engine, question bank
  Data/Feud/                     the ProtoQA starter questions (feud-starter.json)
  Components/Floor/              the Floor screens (TV, emcee, player, lobby, the floor map, topic picker)
  FloorServices.cs               Floor setup: the data folder, the topics passcode, the picture and upload addresses
  wwwroot/js/floor.js            the Floor clocks and the picture uploader

tests/                         Checks that play real games and verify the rules and the screens
Dockerfile                     How Railway builds and runs it
```

## What's next

- Swap the dummy questions for a real dataset (the engine only knows about `IGameDataSource`).
- Postgres, to remember which games have been played so none repeat.
- The next game show.

### The wheel's lettering

Wedge labels are stacked one character under another (like the show) and use the font named by `--wf-wheel-font` in `wwwroot/app.css`. It
asks for **Oldgate Lane Outline** first and falls back to the board font (Oswald) if the font file isn't there. To use it, put the file at
`src/BuzzerGames.Web/wwwroot/fonts/OldgateLaneOutline.ttf` (that exact name). Check the font's license before publishing the repo.
