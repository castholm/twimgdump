namespace TwimgDump
{
    public sealed class TweetMedia
    {
        public string Url { get; set; } = null!;

        public string UserId { get; set; } = null!;
        public string Username { get; set; } = null!;

        public string TweetId { get; set; } = null!;
        public string Created { get; set; } = null!;
        public int Count { get; set; }

        public string MediaId { get; set; } = null!;
        public int Index { get; set; }
        public string BaseName { get; set; } = null!;
        public string Extension { get; set; } = null!;
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
