using System;

namespace Moonlight.Localization
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class BigMultilineAttribute : Attribute
    {
        public int MinLines { get; }
        public int MaxLines { get; }
        
        public BigMultilineAttribute(int minLines = 4, int maxLines = 10)
        {
            MinLines = minLines;
            MaxLines = maxLines;
        }
    }
}