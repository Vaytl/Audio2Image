using Avalonia.Media;

namespace Audio2Image.App.Models;

/// <summary>
/// A single tag for display in gallery pill badges, with category-based color.
/// </summary>
public record TagDisplayItem(string Label, IBrush Background, IBrush Foreground);

/// <summary>
/// Maps AudioSet class labels to visual categories with distinct colors.
/// Colors chosen for contrast against orange/amber spectrogram backgrounds.
/// </summary>
public static class TagCategoryColors
{
    private enum Category { Music, Speech, Nature, Animal, Noise, Machine, Effects, Other }

    // Background + foreground pairs per category
    // Semi-transparent backgrounds for subtlety, bright foregrounds for readability
    private static readonly (IBrush Bg, IBrush Fg)[] Palette =
    [
        /* Music   */ (Brush("#552244AA"), Brush("#AABBFF")),  // lavender/blue — cool contrast on warm spectrogram
        /* Speech  */ (Brush("#5522AACC"), Brush("#88DDEE")),  // cyan
        /* Nature  */ (Brush("#5522AA55"), Brush("#88DD99")),  // green
        /* Animal  */ (Brush("#5566AA22"), Brush("#BBDD77")),  // lime
        /* Noise   */ (Brush("#44666666"), Brush("#AAAAAA")),  // gray
        /* Machine */ (Brush("#55774488"), Brush("#CC99DD")),  // purple
        /* Effects */ (Brush("#55AA4444"), Brush("#FF9999")),  // red-ish
        /* Other   */ (Brush("#44888866"), Brush("#CCCCAA")),  // neutral warm
    ];

    /// <summary>
    /// Get display item for a tag label with category-appropriate colors.
    /// </summary>
    public static TagDisplayItem GetDisplayItem(string label)
    {
        var cat = Categorize(label);
        var (bg, fg) = Palette[(int)cat];
        return new TagDisplayItem(label, bg, fg);
    }

    private static Category Categorize(string label)
    {
        var lower = label.ToLowerInvariant();

        // Music (instruments, genres, singing)
        if (IsAny(lower, "music", "guitar", "piano", "drum", "bass", "violin", "trumpet",
            "flute", "organ", "harmonica", "banjo", "ukulele", "harp", "cello", "saxophone",
            "synthesizer", "keyboard", "accordion", "mandolin", "sitar", "tabla",
            "singing", "choir", "song", "vocal", "beatbox", "rapping", "humming",
            "hip hop", "jazz", "rock", "pop", "blues", "reggae", "funk", "soul",
            "rhythm and blues", "country", "folk", "electronic", "techno", "disco",
            "heavy metal", "punk", "grunge", "ska", "dubstep", "house music",
            "musical instrument", "plucked string", "bowed string", "percussion",
            "mallet percussion", "tuning fork", "orchestra", "symphony"))
            return Category.Music;

        // Speech (voice, language)
        if (IsAny(lower, "speech", "male speech", "female speech", "child speech",
            "conversation", "narration", "monologue", "whispering", "shout",
            "screaming", "yell", "gasp", "sigh", "grunt", "groan",
            "laughter", "giggle", "snicker", "chuckle", "crying", "sobbing",
            "baby cry", "babbling", "burping", "hiccup", "cough", "sneeze",
            "snoring", "breathing", "gargling"))
            return Category.Speech;

        // Nature (weather, water, environment)
        if (IsAny(lower, "rain", "raindrop", "rain on surface", "thunder", "thunderstorm",
            "wind", "rustling", "howl", "ocean", "wave", "stream", "waterfall",
            "drip", "splash", "water", "river", "surf", "fire", "crackle",
            "earthquake", "avalanche", "landslide", "ice", "hail"))
            return Category.Nature;

        // Animal
        if (IsAny(lower, "dog", "bark", "howl", "growl", "cat", "purr", "meow", "hiss",
            "bird", "chirp", "tweet", "crow", "owl", "eagle", "pigeon", "duck", "goose",
            "rooster", "chicken", "frog", "cricket", "insect", "bee", "fly", "mosquito",
            "horse", "cow", "pig", "sheep", "goat", "whale", "dolphin",
            "snake", "lion", "tiger", "bear", "elephant", "monkey", "wolf"))
            return Category.Animal;

        // Noise / silence
        if (IsAny(lower, "noise", "white noise", "pink noise", "static", "hum", "buzz",
            "silence", "ambient", "background noise", "environmental noise"))
            return Category.Noise;

        // Machine / vehicle
        if (IsAny(lower, "engine", "motor", "car", "truck", "bus", "train", "aircraft",
            "helicopter", "boat", "ship", "motorcycle", "bicycle", "vehicle",
            "machine", "mechanical", "drill", "saw", "hammer", "jackhammer",
            "typing", "computer", "printer", "power tool", "chainsaw",
            "lawn mower", "vacuum cleaner", "washing machine", "air conditioning"))
            return Category.Machine;

        // Effects / alerts
        if (IsAny(lower, "explosion", "gunshot", "firework", "alarm", "siren", "bell",
            "doorbell", "telephone", "ring", "click", "beep", "chime", "horn",
            "whistle", "clap", "knock", "slam", "bang", "crash", "thud",
            "squeak", "creak", "glass", "breaking", "shatter", "zipper",
            "keys", "coin", "pour", "stir", "chop", "frying"))
            return Category.Effects;

        return Category.Other;
    }

    private static bool IsAny(string lower, params string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (lower == kw || lower.Contains(kw))
                return true;
        }
        return false;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
