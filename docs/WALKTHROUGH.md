# Walkthrough: reading (and rebuilding) BuzzerGames, piece by piece

This is the order to go through the code so each piece only depends on things you've already seen.
Every stop has the one idea to look for. Type each piece into your own project, run it, and only then move on.

## Part 1: the game, with no web page at all

The whole game is plain C# in `src/BuzzerGames.Core`. You can prove it works without ever opening a browser.

1. **`GameData.cs`**: Four small records. *Idea: describe the questions as plain data, separate from playing the game.*
2. **`IGameDataSource.cs` and `DummyGameDataSource.cs`**: *Idea: depend on an interface, so the questions can come from anywhere later.*
3. **`Results.cs`**: The phases, and the `Result` type. *Idea: "too slow, someone buzzed first" is normal at a party, so actions return a result instead of throwing.*
4. **`JeopardyGame.cs`, in this order:**
   1. The private state at the top (who's in control? who's answering? who's locked out?).
   2. `Run(...)` at the bottom. *Idea: one lock, and one place that announces changes.*
   3. Lobby: `JoinEmcee`, `JoinPlayer`, `Start`.
   4. One clue: `SelectClue` → `Buzz` → `Judge` → `Continue`. *Idea: the game is a state machine; each method checks "am I in the right phase?" first.*
   5. `GiveUp`, then Daily Doubles.
   6. Round 2, then Final Jeopardy.
5. **`Snapshots.cs` and `Snapshot()`**: *Idea: pages draw from a frozen copy, so a buzz arriving mid-draw can't corrupt the screen.*
6. **`GameRoomManager.cs`**: *Idea: one shared object holds every game, found by a short code.*
7. **Run `tests/BuzzerGames.Core.Checks`** and read two or three checks. They are the clearest description of the rules.

## Part 2: putting it on the web

8. **`Program.cs`**: *Idea: dependency injection. We register one `GameRoomManager` and every page can ask for it.*
9. **`Components/Pages/JeopardyRoom.razor`**: *Idea: one page, two questions, "what phase?" and "who is this device?", and the answers pick which view to show.*
10. **`LobbyView.razor`**: The three buttons, the roster, and the QR code. *Idea: `EventCallback` lets a child tell its parent something happened.*
11. **`BoardView.razor`** (the TV), then **`PlayerView.razor`**, then **`EmceeView.razor`**. *Idea: the same snapshot, three different faces. What each device is allowed to see is a design decision.*
12. **`wwwroot/js/bg.js`**: *Idea: JavaScript interop. Blazor calls into the browser for the two things it can't do alone (saved seat, QR code).*
13. **Run `tests/BuzzerGames.Web.Checks`** and read the "never leaks the answer" checks.

## Part 3: getting it online

14. **`Dockerfile`**: Two stages, and why. *Idea: the build image is big; the run image is small.*
15. **Railway**: Connect the repo, generate a domain, open it on your phone.

## Questions to ask yourself at each stop

- What would break if I deleted this?
- What is this code *not* allowed to know about?
- How would I test it?
