using Internal.ReadLine.Abstractions;

using System;
using System.Collections.Generic;
using System.Text;

namespace Internal.ReadLine
{
    internal class KeyHandler
    {
        private int _cursorPos;
        private StringBuilder _text;
        private List<string> _history;
        private int _historyIndex;
        private ConsoleKeyInfo _keyInfo;
        private Dictionary<string, Action> _keyActions;
        private List<string> _completions = new();
        private int _completionStart;
        private int _completionsIndex;
        private bool _passwordMode;
        private IConsole Console2;
        private bool IsStartOfLine() => _cursorPos == 0;
        private bool IsEndOfLine() => _cursorPos == _text.Length;
        private bool IsInAutoCompleteMode() => _completions.Count > 0;

        private void SetCursorPosition(int left, int top)
        {
            var ansiSequence = EscapeSequence.SetPosition(left + 1, top + 1);
            Console2.Write(ansiSequence);
        }

        private void MoveCursorLeft()
        {
            if (IsStartOfLine())
                return;

            if (Console2.CursorLeft == 0)
                SetCursorPosition(Console2.BufferWidth - 1, Console2.CursorTop - 1);
            else
                SetCursorPosition(Console2.CursorLeft - 1, Console2.CursorTop);

            _cursorPos--;
        }

        private void MoveCursorHome()
        {
            while (!IsStartOfLine())
                MoveCursorLeft();
        }

        private string BuildKeyInput()
        {
            return (_keyInfo.Modifiers != ConsoleModifiers.Control && _keyInfo.Modifiers != ConsoleModifiers.Shift) ?
                _keyInfo.Key.ToString() : _keyInfo.Modifiers.ToString() + _keyInfo.Key.ToString();
        }

        private void MoveCursorRight()
        {
            if (IsEndOfLine())
                return;

            if (Console2.CursorLeft == Console2.BufferWidth - 1)
                SetCursorPosition(0, Console2.CursorTop + 1);
            else
                SetCursorPosition(Console2.CursorLeft + 1, Console2.CursorTop);

            _cursorPos++;
        }

        private void MoveCursorEnd()
        {
            while (!IsEndOfLine())
                MoveCursorRight();
        }

        private void ClearLine()
        {
            MoveCursorEnd();
            while (!IsStartOfLine())
                Backspace();
        }

        private void WriteNewString(string str)
        {
            ClearLine();
            foreach (char character in str)
                WriteChar(character);
        }

        private void WriteString(string str)
        {
            foreach (char character in str)
                WriteChar(character);
        }

        private void WriteChar() => WriteChar(_keyInfo.KeyChar);

        private void WriteChar(char c)
        {
            var str_out = c.ToString();

            if (IsEndOfLine())
            {
                _text.Append(c);
                _cursorPos++;
            }
            else
            {
                int left = Console2.CursorLeft;
                int top = Console2.CursorTop;
                str_out += _text.ToString().Substring(_cursorPos);
                _text.Insert(_cursorPos, c);
                SetCursorPosition(left, top);
                MoveCursorRight();
            }

            if (_passwordMode)
                Console2.Write(new string('*', str_out.Length));
            else
                Console2.Write(str_out);
        }

        private void Backspace()
        {
            if (IsStartOfLine())
                return;

            MoveCursorLeft();
            int index = _cursorPos;
            _text.Remove(index, 1);
            string replacement = _text.ToString().Substring(index);
            int left = Console2.CursorLeft;
            int top = Console2.CursorTop;
            Console2.Write(string.Format("{0} ", replacement));
            SetCursorPosition(left, top);
        }

        private void Delete()
        {
            if (IsEndOfLine())
                return;

            int index = _cursorPos;
            _text.Remove(index, 1);
            string replacement = _text.ToString().Substring(index);
            int left = Console2.CursorLeft;
            int top = Console2.CursorTop;
            Console2.Write(string.Format("{0} ", replacement));
            SetCursorPosition(left, top);
        }

        private void TransposeChars()
        {
            // local helper functions
            bool almostEndOfLine() => (_text.Length - _cursorPos) == 1;
            int incrementIf(Func<bool> expression, int index) =>  expression() ? index + 1 : index;
            int decrementIf(Func<bool> expression, int index) => expression() ? index - 1 : index;

            if (IsStartOfLine()) { return; }

            var firstIdx = decrementIf(IsEndOfLine, _cursorPos - 1);
            var secondIdx = decrementIf(IsEndOfLine, _cursorPos);

            var secondChar = _text[secondIdx];
            _text[secondIdx] = _text[firstIdx];
            _text[firstIdx] = secondChar;

            var left = incrementIf(almostEndOfLine, Console2.CursorLeft);
            var cursorPosition = incrementIf(almostEndOfLine, _cursorPos);

            WriteNewString(_text.ToString());

            SetCursorPosition(left, Console2.CursorTop);
            _cursorPos = cursorPosition;

            MoveCursorRight();
        }

        private void StartAutoComplete()
        {
            while (_cursorPos > _completionStart)
                Backspace();

            _completionsIndex = 0;

            WriteString(_completions[_completionsIndex]);
        }

        private void NextAutoComplete()
        {
            while (_cursorPos > _completionStart)
                Backspace();

            _completionsIndex++;

            if (_completionsIndex == _completions.Count)
                _completionsIndex = 0;

            WriteString(_completions[_completionsIndex]);
        }

        private void PreviousAutoComplete()
        {
            while (_cursorPos > _completionStart)
                Backspace();

            _completionsIndex--;

            if (_completionsIndex == -1)
                _completionsIndex = _completions.Count - 1;

            WriteString(_completions[_completionsIndex]);
        }

        private void FirstHistory()
        {
            if (_history.Count > 0)
            {
                _historyIndex = 0;
                WriteNewString(_history[_historyIndex]);
            }
        }

        private void PrevHistory()
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                WriteNewString(_history[_historyIndex]);
            }
        }

        private void NextHistory()
        {
            if (_historyIndex < _history.Count)
            {
                _historyIndex++;
                if (_historyIndex == _history.Count)
                    ClearLine();
                else
                    WriteNewString(_history[_historyIndex]);
            }
        }

        private void ResetAutoComplete()
        {
            _completions.Clear();
            _completionsIndex = 0;
        }

        public string Text
        {
            get
            {
                return _text.ToString();
            }
        }

        public KeyHandler(IConsole console, List<string>? history, IAutoCompleteHandler? autoCompleteHandler)
        {
            Console2 = console;

            _passwordMode = history == null; // history always initiated unless password mode
            _history = history ?? new List<string>();
            _historyIndex = _history.Count;
            _text = new StringBuilder();
            _keyActions = new Dictionary<string, Action>();

            _keyActions["LeftArrow"] = MoveCursorLeft;
            _keyActions["Home"] = MoveCursorHome;
            _keyActions["End"] = MoveCursorEnd;
            _keyActions["ControlA"] = MoveCursorHome;
            _keyActions["ControlB"] = MoveCursorLeft;
            _keyActions["RightArrow"] = MoveCursorRight;
            _keyActions["ControlF"] = MoveCursorRight;
            _keyActions["ControlE"] = MoveCursorEnd;
            _keyActions["Backspace"] = Backspace;
            _keyActions["Delete"] = Delete;
            _keyActions["ControlD"] = Delete;
            _keyActions["ControlH"] = Backspace;
            _keyActions["ControlL"] = ClearLine;
            _keyActions["Escape"] = ClearLine;
            _keyActions["UpArrow"] = PrevHistory;
            _keyActions["ControlP"] = PrevHistory;
            _keyActions["DownArrow"] = NextHistory;
            _keyActions["F3"] = FirstHistory;
            _keyActions["ControlN"] = NextHistory;
            _keyActions["ControlU"] = () =>
            {
                while (!IsStartOfLine())
                    Backspace();
            };
            _keyActions["ControlK"] = () =>
            {
                int pos = _cursorPos;
                MoveCursorEnd();
                while (_cursorPos > pos)
                    Backspace();
            };
            _keyActions["ControlW"] = () =>
            {
                while (!IsStartOfLine() && _text[_cursorPos - 1] != ' ')
                    Backspace();
            };
            _keyActions["ControlHome"] = _keyActions["ControlU"];
            _keyActions["ControlEnd"] = _keyActions["ControlK"];
            _keyActions["ControlBackspace"] = _keyActions["ControlW"];
            _keyActions["ControlLeftArrow"] = () =>
            {
                while (!IsStartOfLine() && _text[_cursorPos - 1] == ' ')
                    MoveCursorLeft();
                while (!IsStartOfLine() && _text[_cursorPos - 1] != ' ')
                    MoveCursorLeft();
            };
            _keyActions["ControlRightArrow"] = () =>
            {
                while (!IsEndOfLine() && _text[_cursorPos] != ' ')
                    MoveCursorRight();
                while (!IsEndOfLine() && _text[_cursorPos] == ' ')
                    MoveCursorRight();
            };

            _keyActions["ControlT"] = TransposeChars;

            _keyActions["Tab"] = () =>
            {
                if (IsInAutoCompleteMode())
                {
                    NextAutoComplete();
                }
                else
                {
                    if (autoCompleteHandler == null || !IsEndOfLine())
                        return;

                    string text = _text.ToString();

                    _completionStart = text.LastIndexOfAny(autoCompleteHandler.Separators);
                    _completionStart = _completionStart == -1 ? 0 : _completionStart + 1;

                    _completions.Clear();
                    var suggestions = autoCompleteHandler.GetSuggestions(text, _completionStart);

                    if (suggestions != null)
                        _completions.AddRange(suggestions);

                    if (IsInAutoCompleteMode())
                        StartAutoComplete();
                }
            };

            _keyActions["ShiftTab"] = () =>
            {
                if (IsInAutoCompleteMode())
                {
                    PreviousAutoComplete();
                }
            };
        }

        public void Handle(ConsoleKeyInfo keyInfo)
        {
            _keyInfo = keyInfo;

            // If in auto complete mode and Tab wasn't pressed
            if (IsInAutoCompleteMode() && _keyInfo.Key != ConsoleKey.Tab)
                ResetAutoComplete();

            Action action;
            _keyActions.TryGetValue(BuildKeyInput(), out action);
            action = action ?? WriteChar;
            action.Invoke();
        }
    }
}
