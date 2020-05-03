using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwimgDump.CommandLine;

namespace TwimgDump
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IList<string> positionalArgs;
            IDictionary<string, IList<string>> opts;
            try
            {
                (positionalArgs, opts) = CommandLineArgsParser.Parse(args, new (string?, string, bool)[]
                {
                    ("c", "cursor", true),
                    ("h", "help", false),
                    ("o", "output", true),
                    ("V", "version", false),
                });
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine($"Error: {exception.Message}");
                PrintUsage();

                return;
            }

            if (opts.TryGetValue("help", out _))
            {
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    "Usage:",
                    "  twimgdump [options] [--] <user-screen-name>",
                    "",
                    "Options:",
                    "  -c, --cursor <cursor>",
                    "    Set the initial cursor value.",
                    "",
                    "  -h, --help",
                    "    Display this help text.",
                    "",
                    "  -o, --output <output-file-path-template>",
                    "    Set the template used to determine the output file paths of",
                    "    downloaded media.  The tokens '[userId]', '[username]', '[tweetId]',",
                    "    '[created]', '[count]', '[mediaId]', '[index]', '[baseName]',",
                    "    '[extension]', '[width]' and '[height]' will be substituted by the",
                    "    attributes of retrieved media.  If not specified, the default",
                    "    template '[username]/[tweetId]+[index].[extension]' will be used.",
                    "",
                    "  -V, --version",
                    "    Display the version number."));

                return;
            }

            if (opts.TryGetValue("version", out _))
            {
                var version = typeof(Program)
                    .Assembly
                    .GetCustomAttribute<AssemblyFileVersionAttribute>()
                    ?.Version
                    ?? "Unknown version.";

                Console.WriteLine(version);

                return;
            }

            var userScreenName = (string?)positionalArgs.ElementAtOrDefault(0);
            if (userScreenName is null)
            {
                PrintUsage();

                return;
            }

            if (positionalArgs.Count > 1)
            {
                Console.WriteLine($"Error: Unknown positional argument '{positionalArgs.ElementAt(1)}'.");
                PrintUsage();

                return;
            }

            var outputPathTemplate = opts.TryGetValue("output", out var outputPathTemplateArgs)
                ? outputPathTemplateArgs.Last()
                : "[username]/[tweetId]+[index].[extension]";

            var currentCursor = opts.TryGetValue("cursor", out var cursorArgs)
                ? cursorArgs.Last()
                : null;

            using var client = new MediaTimelineClient();
            using var downloader = new MediaDownloader();

            IList<TweetMedia> mediaList;
            try
            {
                do
                {
                    Console.WriteLine("Current cursor: {0}", currentCursor ?? "null");

                    string cursorTop;
                    string cursorBottom;
                    (mediaList, cursorTop, cursorBottom) = await client.FetchTweetsAsync(userScreenName, currentCursor);

                    var tweetCount = mediaList.Select(x => x.TweetId).Distinct().Count();

                    Console.WriteLine("Fetched {0} tweet(s).", tweetCount);
                    Console.WriteLine("Next cursor: {0}", cursorBottom);

                    foreach (var media in mediaList)
                    {
                        // Media is intentially downloaded in sequence (as opposed to in parallel) to keep request
                        // rates low and stay clear of potential rate limiting.

                        var file = outputPathTemplate
                            .Replace("[userId]", Sanitize(media.UserId), StringComparison.OrdinalIgnoreCase)
                            .Replace("[username]", Sanitize(media.Username), StringComparison.OrdinalIgnoreCase)
                            .Replace("[tweetId]", Sanitize(media.TweetId), StringComparison.OrdinalIgnoreCase)
                            .Replace("[created]", Sanitize(media.Created), StringComparison.OrdinalIgnoreCase)
                            .Replace("[count]", Sanitize(((uint)media.Count).ToString()), StringComparison.OrdinalIgnoreCase)
                            .Replace("[mediaId]", Sanitize(media.MediaId), StringComparison.OrdinalIgnoreCase)
                            .Replace("[index]", Sanitize(((uint)media.Index).ToString()), StringComparison.OrdinalIgnoreCase)
                            .Replace("[baseName]", Sanitize(media.BaseName), StringComparison.OrdinalIgnoreCase)
                            .Replace("[extension]", Sanitize(media.Extension), StringComparison.OrdinalIgnoreCase)
                            .Replace("[width]", Sanitize(((uint)media.Width).ToString()), StringComparison.OrdinalIgnoreCase)
                            .Replace("[height]", Sanitize(((uint)media.Height).ToString()), StringComparison.OrdinalIgnoreCase);

                        await downloader.DownloadAsync(media.Url, file);
                    }

                    Console.WriteLine("Downloaded {0} file(s).", mediaList.Count);

                    currentCursor = cursorBottom;
                }
                while (mediaList.Any());
            }
            catch (Exception e)
            {
                Console.WriteLine("An unhandled exception was thrown by the application.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);

                return;
            }

            Console.WriteLine("All media has been downloaded.");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: twimgdump [options] [--] <user-screen-name>");
            Console.WriteLine("Try 'twimgdump --help' for more information.");
        }

        private static string Sanitize(string input)
            => Regex.Replace(input, "[^-0-9A-Z_a-z]", "");
    }
}
