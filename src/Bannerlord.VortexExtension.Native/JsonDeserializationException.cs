﻿using System;
using System.Text.Json;

namespace Bannerlord.VortexExtension.Native
{
    public class JsonDeserializationException : JsonException
    {
        public JsonDeserializationException(string message) : base(message) { }
        public JsonDeserializationException(string message, Exception exception) : base(message, exception) { }
    }
}