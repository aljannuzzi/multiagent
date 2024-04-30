namespace Common;
public static class Constants
{
    public static class Token
    {
        public const string EndToken = @"!END!";
    }

    public static class SignalR
    {
        public const string HubName = "TBASignalRHub";

        public static class Users
        {
            public const string EndUser = nameof(EndUser);
            public const string Orchestrator = nameof(Orchestrator);
        }

        public static class Functions
        {
            public const string GetAnswer = nameof(GetAnswer);

            public const string GetStreamedAnswer = nameof(GetStreamedAnswer);

            public const string SendStreamedAnswerBack = nameof(SendStreamedAnswerBack);

            public const string Introduce = nameof(Introduce);

            public const string Reintroduce = nameof(Reintroduce);

            public const string ExpertJoined = nameof(ExpertJoined);

            public const string ExpertLeft = nameof(ExpertLeft);

            public const string PostStatus = nameof(PostStatus);
        }
    }
}
