// Quill for Unity — a declarative, reactive UI framework.
// Copyright (c) 2026 Leo CHAUMARTIN. Licensed under the MIT License - see LICENSE.md.
//
using System.Collections.Generic;

namespace Quill
{
    /// <summary>
    /// Key codes and modifier flags, exposed to documents as <c>Quill.Key_*</c> and
    /// <c>Quill.*Modifier</c> (<c>event.key == Quill.Key_Escape</c>). The values follow Qt's, so code
    /// written against QML's key events reads the same.
    /// </summary>
    public static class QuillKeys
    {
        public const int Key_Escape = 0x01000000, Key_Tab = 0x01000001, Key_Backtab = 0x01000002,
            Key_Backspace = 0x01000003, Key_Return = 0x01000004, Key_Enter = 0x01000005,
            Key_Insert = 0x01000006, Key_Delete = 0x01000007, Key_Home = 0x01000010, Key_End = 0x01000011,
            Key_Left = 0x01000012, Key_Up = 0x01000013, Key_Right = 0x01000014, Key_Down = 0x01000015,
            Key_PageUp = 0x01000016, Key_PageDown = 0x01000017,
            Key_Shift = 0x01000020, Key_Control = 0x01000021, Key_Meta = 0x01000022, Key_Alt = 0x01000023,
            Key_F1 = 0x01000030, Key_Menu = 0x01000055, Key_Back = 0x01000061,
            Key_Space = 0x20, Key_0 = 0x30, Key_A = 0x41, Key_C = 0x43, Key_V = 0x56, Key_X = 0x58;

        public const int NoModifier = 0, ShiftModifier = 0x02000000, ControlModifier = 0x04000000,
            AltModifier = 0x08000000, MetaModifier = 0x10000000;

        /// <summary>Every name registered under the <c>Quill</c> enum object.</summary>
        public static IEnumerable<(string name, double value)> All()
        {
            yield return ("Key_Escape", Key_Escape); yield return ("Key_Tab", Key_Tab);
            yield return ("Key_Backtab", Key_Backtab); yield return ("Key_Backspace", Key_Backspace);
            yield return ("Key_Return", Key_Return); yield return ("Key_Enter", Key_Enter);
            yield return ("Key_Insert", Key_Insert); yield return ("Key_Delete", Key_Delete);
            yield return ("Key_Home", Key_Home); yield return ("Key_End", Key_End);
            yield return ("Key_Left", Key_Left); yield return ("Key_Up", Key_Up);
            yield return ("Key_Right", Key_Right); yield return ("Key_Down", Key_Down);
            yield return ("Key_PageUp", Key_PageUp); yield return ("Key_PageDown", Key_PageDown);
            yield return ("Key_Shift", Key_Shift); yield return ("Key_Control", Key_Control);
            yield return ("Key_Meta", Key_Meta); yield return ("Key_Alt", Key_Alt);
            yield return ("Key_Menu", Key_Menu); yield return ("Key_Back", Key_Back);
            yield return ("Key_Space", Key_Space);
            for (int i = 0; i < 12; i++) yield return ("Key_F" + (i + 1), Key_F1 + i);
            for (int i = 0; i < 10; i++) yield return ("Key_" + i, Key_0 + i);
            for (int i = 0; i < 26; i++) yield return ("Key_" + (char)('A' + i), Key_A + i);
            yield return ("NoModifier", NoModifier); yield return ("ShiftModifier", ShiftModifier);
            yield return ("ControlModifier", ControlModifier); yield return ("AltModifier", AltModifier);
            yield return ("MetaModifier", MetaModifier);
        }

        /// <summary>
        /// The specific <c>Keys.on…Pressed</c> handler for a key (as in QML), or null. It runs before
        /// <c>Keys.onPressed</c>, with the event already accepted.
        /// </summary>
        public static string SpecificHandler(int key)
        {
            switch (key)
            {
                case Key_Return: return "Keys.onReturnPressed";
                case Key_Enter: return "Keys.onEnterPressed";
                case Key_Escape: return "Keys.onEscapePressed";
                case Key_Space: return "Keys.onSpacePressed";
                case Key_Tab: return "Keys.onTabPressed";
                case Key_Backtab: return "Keys.onBacktabPressed";
                case Key_Up: return "Keys.onUpPressed";
                case Key_Down: return "Keys.onDownPressed";
                case Key_Left: return "Keys.onLeftPressed";
                case Key_Right: return "Keys.onRightPressed";
                case Key_Delete: return "Keys.onDeletePressed";
                case Key_Back: return "Keys.onBackPressed";
                case Key_Menu: return "Keys.onMenuPressed";
            }
            if (key >= Key_0 && key <= Key_0 + 9) return s_Digits[key - Key_0];
            return null;
        }

        private static readonly string[] s_Digits =
        {
            "Keys.onDigit0Pressed", "Keys.onDigit1Pressed", "Keys.onDigit2Pressed", "Keys.onDigit3Pressed",
            "Keys.onDigit4Pressed", "Keys.onDigit5Pressed", "Keys.onDigit6Pressed", "Keys.onDigit7Pressed",
            "Keys.onDigit8Pressed", "Keys.onDigit9Pressed",
        };

        /// <summary>The names <c>Keys.*</c> accepts as handlers (for tooling).</summary>
        public static readonly string[] HandlerNames =
        {
            "onPressed", "onReleased", "onReturnPressed", "onEnterPressed", "onEscapePressed", "onSpacePressed",
            "onTabPressed", "onBacktabPressed", "onUpPressed", "onDownPressed", "onLeftPressed", "onRightPressed",
            "onDeletePressed", "onBackPressed", "onMenuPressed",
            "onDigit0Pressed", "onDigit1Pressed", "onDigit2Pressed", "onDigit3Pressed", "onDigit4Pressed",
            "onDigit5Pressed", "onDigit6Pressed", "onDigit7Pressed", "onDigit8Pressed", "onDigit9Pressed",
        };

        /// <summary>Text a key produces on its own (letters, digits, space), for <c>event.text</c>.</summary>
        public static string TextOf(int key, int modifiers)
        {
            if ((modifiers & (ControlModifier | AltModifier | MetaModifier)) != 0) return "";
            bool shift = (modifiers & ShiftModifier) != 0;
            if (key >= Key_A && key < Key_A + 26) return ((char)((shift ? 'A' : 'a') + key - Key_A)).ToString();
            if (key >= Key_0 && key < Key_0 + 10 && !shift) return ((char)('0' + key - Key_0)).ToString();
            if (key == Key_Space) return " ";
            return "";
        }
    }
}
