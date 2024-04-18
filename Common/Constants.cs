namespace Common;
public static class Constants
{
    public static class SignalR
    {
        public const string HubName = "TBASignalRHub";

        public static class Categories
        {
            public const string Connections = "connections";
            public const string Messages = "messages";
        }

        public static class Events
        {
            public const string NewMessage = "newMessage";
        }

        public static class Users
        {
            public const string EndUser = nameof(EndUser);
            public const string Orchestrator = nameof(Orchestrator);

            public const string ConnectionCache = nameof(ConnectionCache);

            public static class Experts
            {
                public const string Teams = nameof(Teams);
            }
        }

        public static class Functions
        {
            public const string Hello = nameof(Hello);

            public const string RegisterConnectionId = nameof(RegisterConnectionId);

            public const string GetAnswer = nameof(GetAnswer);

            public const string GetUserConnectionId = nameof(GetUserConnectionId);

            public const string RegisterCacheConnection = nameof(RegisterCacheConnection);

            public const string Introduce = nameof(Introduce);

            public const string AskExpert = nameof(AskExpert);

            public const string ExpertAnswerReceived = nameof(ExpertAnswerReceived);

            public const string ExpertJoined = nameof(ExpertJoined);

            public const string ExpertLeft = nameof(ExpertLeft);
        }
    }
}
