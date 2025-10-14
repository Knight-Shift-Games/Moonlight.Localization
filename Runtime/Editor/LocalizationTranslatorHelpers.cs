using System;
using System.Collections.Generic;

namespace Moonlight.Localization
{
    [Serializable]
    public class OpenAIAPIBody
    {
        public string model;
        public List<Message> messages;
    }

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    // For Deserializing the Response
    [Serializable]
    public class OpenAIAPIResponse
    {
        public List<Choice> choices;
    }

    [Serializable]
    public class Choice
    {
        public Message message;
    }
}