using System;

namespace TwimgDump
{
    public sealed class TweetMedia
    {
        public TweetMedia(
            Uri url,
            ulong userId,
            string username,
            ulong tweetId,
            DateTimeOffset created,
            ulong mediaId,
            string stem,
            string extension,
            int index,
            int count,
            int width,
            int height)
        {
            Url = url;
            UserId = userId;
            Username = username;
            TweetId = tweetId;
            Created = created;
            MediaId = mediaId;
            Stem = stem;
            Extension = extension;
            Index = index;
            Count = count;
            Width = width;
            Height = height;
        }

        public Uri Url { get; }

        public ulong UserId { get; }
        public string Username { get; }

        public ulong TweetId { get; }
        public DateTimeOffset Created { get; }

        public ulong MediaId { get; }
        public string Stem { get; }
        public string Extension { get; }
        public int Index { get; }
        public int Count { get; }
        public int Width { get; }
        public int Height { get; }
    }
}
