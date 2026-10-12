using System;

namespace OPLServerBanners;

internal static class MessageBoardProtocol
{
    public const string ClientToServerListener = "doma_message_board_client";
    public const string ServerToClientListener = "doma_message_board_server";

    public const string ActionRequestBoard = "request_board";

    [Serializable]
    internal sealed class Request
    {
        public string Action = ActionRequestBoard;
    }

    [Serializable]
    internal sealed class Response
    {
        public bool Success;
        public string Revision = string.Empty;
        public string BoardJson = string.Empty;
        public string ErrorMessage = string.Empty;
    }
}
