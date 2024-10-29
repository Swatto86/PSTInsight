using System.Runtime.CompilerServices;
using XstReader;

namespace PSTInsight
{
    /// <summary>
    /// Provides extension methods for XstMessage to track export selection state
    /// </summary>
    public static class XstMessageExtensions
    {
        // Using ConditionalWeakTable ensures we don't create memory leaks
        // The selection state will be garbage collected when the XstMessage is
        private static readonly ConditionalWeakTable<XstMessage, SelectionState> SelectionStorage =
            new ConditionalWeakTable<XstMessage, SelectionState>();

        public static bool GetIsSelectedForExport(this XstMessage message)
        {
            var state = SelectionStorage.GetOrCreateValue(message);
            return state.IsSelected;
        }

        public static void SetIsSelectedForExport(this XstMessage message, bool value)
        {
            var state = SelectionStorage.GetOrCreateValue(message);
            state.IsSelected = value;
        }

        private class SelectionState
        {
            public bool IsSelected { get; set; }
        }
    }
}