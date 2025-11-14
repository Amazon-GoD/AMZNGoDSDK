using System;

namespace AMZNGoDSDK.Runtime
{
    public static class IntExtensions
    {
        public static bool AsBool(this int value)
        {
            switch (value)
            {
                case 0: return false;
                case 1: return true;
                default:  throw new ArgumentException("Invalid value. Must be either 0 or 1.");
            }
        }
    }
}