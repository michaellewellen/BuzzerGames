namespace BuzzerGames.Core.Jeopardy;

/// <summary>
/// Placeholder questions so the game is playable before we have a real dataset.
/// Every game draws 12 of the 14 categories below (6 per round) plus one of the final clues.
/// All of this is original trivia written for testing, so it is safe to keep in the repo.
/// </summary>
public sealed class DummyGameDataSource : IGameDataSource
{
    public GameData CreateGame(Random rng, GameOptions? options = null)
    {
        var shuffled = Pool.OrderBy(_ => rng.Next()).ToList();
        var final = Finals[rng.Next(Finals.Count)];
        return new GameData(
            Round1: shuffled.Take(6).ToList(),
            Round2: shuffled.Skip(6).Take(6).ToList(),
            Final: final);
    }

    // Small helper so the data below stays readable: C("clue text", "answer")
    private static ClueData C(string text, string answer) => new(text, answer);

    private static readonly IReadOnlyList<CategoryData> Pool =
    [
        new("STATE CAPITALS",
        [
            C("California's capital, in the Central Valley, is not Los Angeles or San Francisco", "Sacramento"),
            C("The capital of Texas", "Austin"),
            C("The capital of Utah", "Salt Lake City"),
            C("This Mississippi capital shares its name with the 7th U.S. president", "Jackson"),
            C("Juneau is the capital of this state, and no roads connect the city to the rest of it", "Alaska"),
        ]),

        new("SOLAR SYSTEM",
        [
            C("Earth is this number of planet from the Sun", "Third"),
            C("The largest planet in our solar system", "Jupiter"),
            C("This planet is famous for its rings", "Saturn"),
            C("This body was reclassified from planet to dwarf planet in 2006", "Pluto"),
            C("Mars has two moons: Phobos and this one", "Deimos"),
        ]),

        new("HOLIDAY TRIVIA",
        [
            C("The color of Santa's suit", "Red"),
            C("These animals pull Santa's sleigh", "Reindeer"),
            C("Ebenezer Scrooge is the main character of this Charles Dickens novella", "A Christmas Carol"),
            C("In \"A Visit from St. Nicholas,\" this reindeer follows Dasher, Dancer, Prancer, and Vixen", "Comet"),
            C("This Christmas plant, native to Mexico, has red leaves often mistaken for flower petals", "Poinsettia"),
        ]),

        new("WORLD GEOGRAPHY",
        [
            C("The largest ocean on Earth", "Pacific Ocean"),
            C("The longest river in Africa", "The Nile"),
            C("This desert covers much of northern Africa", "The Sahara"),
            C("Mount Everest sits on the border of Nepal and this country", "China"),
            C("The smallest country in the world by area, it sits inside the city of Rome", "Vatican City"),
        ]),

        new("FAMOUS SCIENTISTS",
        [
            C("He developed the theory of relativity", "Albert Einstein"),
            C("She won Nobel Prizes in both physics and chemistry", "Marie Curie"),
            C("He published the Principia in 1687, describing the laws of motion and gravity", "Isaac Newton"),
            C("His 1859 book \"On the Origin of Species\" introduced natural selection", "Charles Darwin"),
            C("This Russian chemist published the first widely recognized periodic table in 1869", "Dmitri Mendeleev"),
        ]),

        new("ANIMALS",
        [
            C("The tallest animal in the world", "Giraffe"),
            C("A group of lions is called this", "A pride"),
            C("The only mammal capable of true, sustained flight", "Bat"),
            C("An octopus has this many hearts", "Three"),
            C("A group of crows is called this ominous-sounding word", "A murder"),
        ]),

        new("FOOD & DRINK",
        [
            C("Pizza originated in this country", "Italy"),
            C("Guacamole is made primarily from this fruit", "Avocado"),
            C("This spice, from the crocus flower, is the most expensive by weight", "Saffron"),
            C("This fermented soybean paste is the base of a popular Japanese soup", "Miso"),
            C("This French term describes cooking food in a sealed bag in a water bath", "Sous vide"),
        ]),

        new("MOVIE NIGHT",
        [
            C("In \"Finding Nemo,\" Nemo is this type of fish", "Clownfish"),
            C("This 1997 film about an ill-fated ocean liner won 11 Academy Awards", "Titanic"),
            C("Dorothy and her dog Toto travel to the Land of Oz in this 1939 musical", "The Wizard of Oz"),
            C("He directed both \"Jaws\" and \"E.T. the Extra-Terrestrial\"", "Steven Spielberg"),
            C("This 1941 Orson Welles film about Charles Foster Kane is often ranked among the greatest ever made", "Citizen Kane"),
        ]),

        new("U.S. PRESIDENTS",
        [
            C("He was the first president of the United States", "George Washington"),
            C("The 16th president, he delivered the Gettysburg Address", "Abraham Lincoln"),
            C("He wrote most of the Declaration of Independence and became the 3rd president", "Thomas Jefferson"),
            C("The only president elected to more than two terms", "Franklin D. Roosevelt"),
            C("The only president to resign from office", "Richard Nixon"),
        ]),

        new("THE HUMAN BODY",
        [
            C("This organ pumps blood through the body", "The heart"),
            C("A full set of adult teeth, wisdom teeth included, has this many", "32"),
            C("The largest organ of the human body", "The skin"),
            C("The smallest bone in the human body is found in this part of the body", "The ear"),
            C("This gland at the base of the neck regulates metabolism", "The thyroid"),
        ]),

        new("WORLD CAPITALS",
        [
            C("The capital of France", "Paris"),
            C("The capital of Japan", "Tokyo"),
            C("The capital of Canada", "Ottawa"),
            C("The capital of Australia, and it is not Sydney", "Canberra"),
            C("The capital of Kazakhstan", "Astana"),
        ]),

        new("ART & COLOR",
        [
            C("Mixing blue and yellow makes this color", "Green"),
            C("He painted the Mona Lisa", "Leonardo da Vinci"),
            C("This Spanish artist co-founded Cubism with Georges Braque", "Pablo Picasso"),
            C("This 1889 Van Gogh painting shows a swirling night sky over a village", "The Starry Night"),
            C("This Dutch painter is known for \"Girl with a Pearl Earring\"", "Johannes Vermeer"),
        ]),

        new("MUSIC",
        [
            C("A standard piano has this many keys", "88"),
            C("This instrument has six strings and is often strummed", "Guitar"),
            C("This musical term means a gradual increase in loudness", "Crescendo"),
            C("Beethoven composed nine of these large orchestral works", "Symphonies"),
            C("This Austrian prodigy composed \"The Magic Flute\" and \"Don Giovanni\"", "Wolfgang Amadeus Mozart"),
        ]),

        new("SPORTS",
        [
            C("In basketball, a free throw is worth this many points", "One"),
            C("A soccer team has this many players on the field", "Eleven"),
            C("This tennis tournament is played on grass in London", "Wimbledon"),
            C("The Summer Olympic Games are held every this many years", "Four"),
            C("In golf, finishing a hole one stroke under par is called this", "A birdie"),
        ]),
    ];

    private static readonly IReadOnlyList<FinalClueData> Finals =
    [
        new("WORLD HISTORY", "This wall, begun in 1961, divided a European city for 28 years", "The Berlin Wall"),
        new("ELEMENTS", "This is the chemical symbol for gold", "Au"),
        new("AUTHORS", "This English playwright wrote \"Romeo and Juliet\" and \"Hamlet\"", "William Shakespeare"),
    ];
}
