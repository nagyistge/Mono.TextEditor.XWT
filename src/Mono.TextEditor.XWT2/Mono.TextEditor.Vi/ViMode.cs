//
// ViMode.cs
//
// Author:
//   Michael Hutchinson <mhutchinson@novell.com>
//
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using Xwt;

namespace Mono.TextEditor.Vi
{
	public class ViEditMode : EditMode
	{
		bool searchBackward;
		static string lastPattern;
		static string lastReplacement;
		State curState;
		State CurState {
			get {
				return curState;
			}
			set {
				curState = value;
				if (statusArea != null) {
					statusArea.ShowCaret = curState == State.Command;
				}
			}
		}

		Motion motion;
		const string substMatch = @"^:s(?<sep>.)(?<pattern>.+?)\k<sep>(?<replacement>.*?)(\k<sep>(?<trailer>i?))?$";
		StringBuilder commandBuffer = new StringBuilder ();
		Dictionary<char,ViMark> marks = new Dictionary<char, ViMark>();
		Dictionary<char,ViMacro> macros = new Dictionary<char, ViMacro>();
		char macros_lastplayed = '@'; // start with the illegal macro character
		
		/// <summary>
		/// The macro currently being implemented. Will be set to null and checked as a flag when required.
		/// </summary>
		ViMacro currentMacro;
		
		public virtual string Status {
		
			get {
				return statusArea.Message;
			}
			
			protected set {
				if (currentMacro != null) {
					value = value + " recording";
				}
				if (viTextEditor != null) {
					statusArea.Message = value;
				}
			}
		}
		
		protected virtual string RunExCommand (string command)
		{
			switch (command[0]) {
			case ':':
				if (2 > command.Length)
					break;
					
				int line;
				if (int.TryParse (command.Substring (1), out line)) {
					if (line < DocumentLocation.MinLine || line > Data.Document.LineCount) {
						return "Invalid line number.";
					} else if (line == 0) {
						RunAction (CaretMoveActions.ToDocumentStart);
						return "Jumped to beginning of document.";
					}
					
					Data.Caret.Line = line;
					Editor.ScrollToCaret ();
					return string.Format ("Jumped to line {0}.", line);
				}
	
				switch (command[1]) {
				case 's':
					if (2 == command.Length) {
						if (null == lastPattern || null == lastReplacement)
							return "No stored pattern.";
							
						// Perform replacement with stored stuff
						command = string.Format (":s/{0}/{1}/", lastPattern, lastReplacement);
					}
		
					var match = Regex.Match (command, substMatch, RegexOptions.Compiled);
					if (!(match.Success && match.Groups["pattern"].Success && match.Groups["replacement"].Success))
						break;
		
					return RegexReplace (match);
					
				case '$':
					if (command.Length == 2) {
						RunAction (CaretMoveActions.ToDocumentEnd);
						return "Jumped to end of document.";
					}
					break;	
				}
				break;
				
			case '?':
			case '/':
				searchBackward = ('?' == command[0]);
				if (1 < command.Length) {
					Editor.HighlightSearchPattern = true;
					Editor.SearchEngine = new RegexSearchEngine ();
					var pattern = command.Substring (1);
					Editor.SearchPattern = pattern;
					var caseSensitive = pattern.ToCharArray ().Any (c => char.IsUpper (c));
					Editor.SearchEngine.SearchRequest.CaseSensitive = caseSensitive;
				}
				return Search ();
			}
			
			return "Command not recognised";
		}
		
		void SearchWordAtCaret ()
		{
			Editor.SearchEngine = new RegexSearchEngine ();
			var s = Data.FindCurrentWordStart (Data.Caret.Offset);
			var e = Data.FindCurrentWordEnd (Data.Caret.Offset);
			if (s < 0 || e <= s)
				return;
			
			var word = Document.GetTextBetween (s, e);
			//use negative lookahead and lookbehind for word characters to make sure we only get fully matching words
			word = "(?<!\\w)" + System.Text.RegularExpressions.Regex.Escape (word) + "(?!\\w)";
			Editor.SearchPattern = word;
			Editor.SearchEngine.SearchRequest.CaseSensitive = true;
			searchBackward = false;
			Search ();
		}
		
		public override bool WantsToPreemptIM {
			get {
				return CurState != State.Insert && CurState != State.Replace;
			}
		}
		
		protected override void SelectionChanged ()
		{
			if (Data.IsSomethingSelected) {
				CurState = ViEditMode.State.Visual;
				Status = "-- VISUAL --";
			} else if (CurState == State.Visual && !Data.IsSomethingSelected) {
				Reset ("");
			}
		}
		
		protected override void CaretPositionChanged ()
		{
			if (CurState == State.Replace || CurState == State.Insert || CurState == State.Visual)
				return;
			else if (CurState == ViEditMode.State.Normal || CurState == ViEditMode.State.Unknown)
				ViActions.RetreatFromLineEnd (Data);
			else
				Reset ("");
		}

		ViStatusArea statusArea;
		TextEditor viTextEditor;

		void CheckVisualMode ()
		{
			if (CurState == ViEditMode.State.Visual || CurState == ViEditMode.State.Visual) {
				if (!Data.IsSomethingSelected)
					CurState = ViEditMode.State.Normal;
			} else {
				if (Data.IsSomethingSelected) {
					CurState = ViEditMode.State.Visual;
					Status = "-- VISUAL --";
				}
			}
		}
		
		void ResetEditorState (TextEditorData data)
		{
			if (data == null)
				return;
			data.ClearSelection ();
			
			//Editor can be null during GUI-less tests
			// Commenting this fixes bug: Bug 622618 - Inline search fails in vi mode
//			if (Editor != null)
//				Editor.HighlightSearchPattern = false;
			
			if (CaretMode.Block != data.Caret.Mode) {
				data.Caret.Mode = CaretMode.Block;
				if (data.Caret.Column > DocumentLocation.MinColumn)
					data.Caret.Column--;
			}
			ViActions.RetreatFromLineEnd (data);
		}
		
		protected override void OnAddedToEditor (TextEditorData data)
		{
			data.Caret.Mode = CaretMode.Block;
			ViActions.RetreatFromLineEnd (data);

			viTextEditor = data.Parent;
			if (viTextEditor != null) {
				statusArea = new ViStatusArea (viTextEditor);
			}
		}
		
		protected override void OnRemovedFromEditor (TextEditorData data)
		{
			data.Caret.Mode = CaretMode.Insert;

			if (viTextEditor != null) {
				statusArea.RemoveFromParentAndDestroy ();
				statusArea = null;
				viTextEditor = null;
			}
		}
		/*TODO
		public override void AllocateTextArea (TextEditor textEditor, TextArea textArea, Rectangle allocation)
		{
			statusArea.AllocateArea (textArea, allocation);
		}*/
		
		void Reset (string status)
		{
			CurState = State.Normal;
			ResetEditorState (Data);
			
			commandBuffer.Length = 0;
			Status = status;
		}
		
		protected virtual Action<TextEditorData> GetInsertAction (Key key, ModifierKeys modifier)
		{
			return ViActionMaps.GetInsertKeyAction (key, modifier) ??
				ViActionMaps.GetDirectionKeyAction (key, modifier);
		}

		protected override void HandleKeypress (Key key, uint unicodeKey, ModifierKeys modifier)
		{
		
			// Reset on Esc, Ctrl-C, Ctrl-[
			if (key == Key.Escape) {
				if (currentMacro != null) {
					// Record Escapes into the macro since it actually does something
					ViMacro.KeySet toAdd = new ViMacro.KeySet();
					toAdd.Key = key;
					toAdd.Modifiers = modifier;
					toAdd.UnicodeKey = unicodeKey;
					currentMacro.KeysPressed.Enqueue(toAdd);
				}
				Reset(string.Empty);
				return;
			} else if (((key == Key.c || false/*TODO key == Gdk.Key.bracketleft*/) && (modifier & ModifierKeys.Control) != 0)) {
				Reset (string.Empty);
				if (currentMacro != null) {
					// Otherwise remove the macro from the pool
					macros.Remove(currentMacro.MacroCharacter);
					currentMacro = null;
				}
				return;
			} else if (currentMacro != null && !((char)unicodeKey == 'q' && modifier == ModifierKeys.None)) {
				ViMacro.KeySet toAdd = new ViMacro.KeySet();
				toAdd.Key = key;
				toAdd.Modifiers = modifier;
				toAdd.UnicodeKey = unicodeKey;
				currentMacro.KeysPressed.Enqueue(toAdd);
			}
			
			Action<TextEditorData> action = null;
			bool lineAction = false;
			
			switch (CurState) {
			case State.Unknown:
				Reset (string.Empty);
				goto case State.Normal;
			case State.Normal:
				if (((modifier & (ModifierKeys.Control)) == 0)) {
					if (key == Key.Delete)
						unicodeKey = 'x';
					switch ((char)unicodeKey) {
					case '?':
					case '/':
					case ':':
						CurState = State.Command;
						commandBuffer.Append ((char)unicodeKey);
						Status = commandBuffer.ToString ();
						return;
					
					case 'A':
						RunAction (CaretMoveActions.LineEnd);
						goto case 'i';
						
					case 'I':
						RunAction (CaretMoveActions.LineFirstNonWhitespace);
						goto case 'i';
					
					case 'a':
						//use CaretMoveActions so that we can move past last character on line end
						RunAction (CaretMoveActions.Right);
						goto case 'i';
					case 'i':
						Caret.Mode = CaretMode.Insert;
						Status = "-- INSERT --";
						CurState = State.Insert;
						return;
						
					case 'R':
						Caret.Mode = CaretMode.Underscore;
						Status = "-- REPLACE --";
						CurState = State.Replace;
						return;

					case 'V':
						Status = "-- VISUAL LINE --";
						Data.SetSelectLines (Caret.Line, Caret.Line);
						CurState = State.VisualLine;
						return;
						
					case 'v':
						Status = "-- VISUAL --";
						CurState = State.Visual;
						RunAction (ViActions.VisualSelectionFromMoveAction (ViActions.Right));
						return;
						
					case 'd':
						Status = "d";
						CurState = State.Delete;
						return;
						
					case 'y':
						Status = "y";
						CurState = State.Yank;
						return;

					case 'Y':
						CurState = State.Yank;
						HandleKeypress (Key.y, (int)'y', ModifierKeys.None);
						return;
						
					case 'O':
						RunAction (ViActions.NewLineAbove);
						goto case 'i';
						
					case 'o':
						RunAction (ViActions.NewLineBelow);
						goto case 'i';
						
					case 'r':
						Caret.Mode = CaretMode.Underscore;
						Status = "-- REPLACE --";
						CurState = State.WriteChar;
						return;
						
					case 'c':
						Caret.Mode = CaretMode.Insert;
						Status = "c";
						CurState = State.Change;
						return;
						
					case 'x':
						if (Data.Caret.Column == Data.Document.GetLine (Data.Caret.Line).Length + 1)
							return;
						Status = string.Empty;
						if (!Data.IsSomethingSelected)
							RunActions (SelectionActions.FromMoveAction (CaretMoveActions.Right), ClipboardActions.Cut);
						else
							RunAction (ClipboardActions.Cut);
						ViActions.RetreatFromLineEnd (Data);
						return;
						
					case 'X':
						if (Data.Caret.Column == DocumentLocation.MinColumn)
							return;
						Status = string.Empty;
						if (!Data.IsSomethingSelected && 0 < Caret.Offset)
							RunActions (SelectionActions.FromMoveAction (CaretMoveActions.Left), ClipboardActions.Cut);
						else
							RunAction (ClipboardActions.Cut);
						return;
						
					case 'D':
						RunActions (SelectionActions.FromMoveAction (CaretMoveActions.LineEnd), ClipboardActions.Cut);
						return;
						
					case 'C':
						RunActions (SelectionActions.FromMoveAction (CaretMoveActions.LineEnd), ClipboardActions.Cut);
						goto case 'i';
						
					case '>':
						Status = ">";
						CurState = State.Indent;
						return;
						
					case '<':
						Status = "<";
						CurState = State.Unindent;
						return;
					case 'n':
						Search ();
						return;
					case 'N':
						searchBackward = !searchBackward;
						Search ();
						searchBackward = !searchBackward;
						return;
					case 'p':
						PasteAfter (false);
						return;
					case 'P':
						PasteBefore (false);
						return;
					case 's':
						if (!Data.IsSomethingSelected)
							RunAction (SelectionActions.FromMoveAction (CaretMoveActions.Right));
						RunAction (ClipboardActions.Cut);
						goto case 'i';
					case 'S':
						if (!Data.IsSomethingSelected)
							RunAction (SelectionActions.LineActionFromMoveAction (CaretMoveActions.LineEnd));
						else Data.SetSelectLines (Data.MainSelection.Anchor.Line, Data.Caret.Line);
						RunAction (ClipboardActions.Cut);
						goto case 'i';
						
					case 'g':
						Status = "g";
						CurState = State.G;
						return;
						
					case 'H':
						Caret.Line = System.Math.Max (DocumentLocation.MinLine, Editor.PointToLocation (0, Editor.LineHeight - 1).Line);
						return;
					case 'J':
						RunAction (ViActions.Join);
						return;
					case 'L':
						var line = Editor.PointToLocation (0, Editor.Size.Height - Editor.LineHeight * 2 - 2).Line;
						if (line < DocumentLocation.MinLine)
							line = Document.LineCount;
						Caret.Line = line;
						return;
					case 'M':
						line = Editor.PointToLocation (0, Editor.Size.Height/2).Line;
						if (line < DocumentLocation.MinLine)
							line = Document.LineCount;
						Caret.Line = line;
						return;
						
					case '~':
						RunAction (ViActions.ToggleCase);
						return;
						
					case 'z':
						Status = "z";
						CurState = State.Fold;
						return;
						
					case 'm':
						Status = "m";
						CurState = State.Mark;
						return;
						
					case '`':
						Status = "`";
						CurState = State.GoToMark;
						return;
						
					case '@':
						Status = "@";
						CurState = State.PlayMacro;
						return;
	
					case 'q':
						if (currentMacro == null) {
							Status = "q";
							CurState = State.NameMacro;
							return;
						} 
						currentMacro = null;
						Reset("Macro Recorded");
						return;
					case '*':
						SearchWordAtCaret ();
						return;
					}
					
				}
				
				action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
				if (action == null)
					action = ViActionMaps.GetDirectionKeyAction (key, modifier);
				if (action == null)
					action = ViActionMaps.GetCommandCharAction ((char)unicodeKey);
				
				if (action != null)
					RunAction (action);
				
				//undo/redo may leave MD with a selection mode without activating visual mode
				CheckVisualMode ();
				return;
				
			case State.Delete:
				if (IsInnerOrOuterMotionKey (unicodeKey, ref motion)) return;

				if (motion != Motion.None) {
					action = ViActionMaps.GetEditObjectCharAction((char) unicodeKey, motion);
				}
				else if (((modifier & (ModifierKeys.Shift | ModifierKeys.Control)) == 0 
				     && unicodeKey == 'd'))
				{
					action = SelectionActions.LineActionFromMoveAction (CaretMoveActions.LineEnd);
					lineAction = true;
				} else {
					action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
					if (action == null)
						action = ViActionMaps.GetDirectionKeyAction (key, modifier);
					if (action != null)
						action = SelectionActions.FromMoveAction (action);
				}
				
				if (action != null) {
					if (lineAction)
						RunActions (action, ClipboardActions.Cut, CaretMoveActions.LineFirstNonWhitespace);
					else
						RunActions (action, ClipboardActions.Cut);
					Reset ("");
				} else {
					Reset ("Unrecognised motion");
				}
				
				return;

			case State.Yank:
				if (IsInnerOrOuterMotionKey (unicodeKey, ref motion)) return;
				int offset = Caret.Offset;

				if (motion != Motion.None) {
					action = ViActionMaps.GetEditObjectCharAction((char) unicodeKey, motion);
				}
				else if (((modifier & (ModifierKeys.Shift | ModifierKeys.Control)) == 0
				     && unicodeKey == 'y'))
				{
					action = SelectionActions.LineActionFromMoveAction (CaretMoveActions.LineEnd);
					lineAction	= true;
				} else {
					action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
					if (action == null)
						action = ViActionMaps.GetDirectionKeyAction (key, modifier);
					if (action != null)
						action = SelectionActions.FromMoveAction (action);
				}
				
				if (action != null) {
					RunAction (action);
					if (Data.IsSomethingSelected && !lineAction)
						offset = Data.SelectionRange.Offset;
					RunAction (ClipboardActions.Copy);
					Reset (string.Empty);
				} else {
					Reset ("Unrecognised motion");
				}
				Caret.Offset = offset;
				
				return;
				
			case State.Change:
				if (IsInnerOrOuterMotionKey (unicodeKey, ref motion)) return;

				if (motion != Motion.None) {
					action = ViActionMaps.GetEditObjectCharAction((char) unicodeKey, motion);
				}
				//copied from delete action
				else if (((modifier & (ModifierKeys.Shift | ModifierKeys.Control)) == 0 
				     && unicodeKey == 'c'))
				{
					action = SelectionActions.LineActionFromMoveAction (CaretMoveActions.LineEnd);
					lineAction = true;
				} else {
					action = ViActionMaps.GetEditObjectCharAction ((char)unicodeKey);
					if (action == null)
						action = ViActionMaps.GetDirectionKeyAction (key, modifier);
					if (action != null)
						action = SelectionActions.FromMoveAction (action);
				}
				
				if (action != null) {
					if (lineAction)
						RunActions (action, ClipboardActions.Cut, ViActions.NewLineAbove);
					else
						RunActions (action, ClipboardActions.Cut);
					Status = "-- INSERT --";
					CurState = State.Insert;
					Caret.Mode = CaretMode.Insert;
				} else {
					Reset ("Unrecognised motion");
				}
				
				return;
				
			case State.Insert:
			case State.Replace:
				action = GetInsertAction (key, modifier);
				
				if (action != null)
					RunAction (action);
				else if (unicodeKey != 0)
					InsertCharacter (unicodeKey);
				
				return;

			case State.VisualLine:
				if (key == Key.Delete)
					unicodeKey = 'x';
				switch ((char)unicodeKey) {
				case 'p':
					PasteAfter (true);
					return;
				case 'P':
					PasteBefore (true);
					return;
				}
				action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
				if (action == null) {
					action = ViActionMaps.GetDirectionKeyAction (key, modifier);
				}
				if (action == null) {
					action = ViActionMaps.GetCommandCharAction ((char)unicodeKey);
				}
				if (action != null) {
					RunAction (SelectionActions.LineActionFromMoveAction (action));
					return;
				}

				ApplyActionToSelection (modifier, unicodeKey);
				return;

			case State.Visual:
				if (IsInnerOrOuterMotionKey (unicodeKey, ref motion)) return;

				if (motion != Motion.None) {
					action = ViActionMaps.GetEditObjectCharAction((char) unicodeKey, motion);
					if (action != null) {
						RunAction (action);
						return;
					}
				}

				if (key == Key.Delete)
					unicodeKey = 'x';
				switch ((char)unicodeKey) {
				case 'p':
					PasteAfter (false);
					return;
				case 'P':
					PasteBefore (false);
					return;
				}
				action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
				if (action == null) {
					action = ViActionMaps.GetDirectionKeyAction (key, modifier);
				}
				if (action == null) {
					action = ViActionMaps.GetCommandCharAction ((char)unicodeKey);
				}
				if (action != null) {
					RunAction (ViActions.VisualSelectionFromMoveAction (action));
					return;
				}

				ApplyActionToSelection (modifier, unicodeKey);
				return;
				
			case State.Command:
				switch (key) {
				case Key.Return:
				case Key.NumPadEnter:
					Status = RunExCommand (commandBuffer.ToString ());
					commandBuffer.Length = 0;
					CurState = State.Normal;
					break;
				case Key.BackSpace:
				case Key.Delete:
				case Key.NumPadDelete:
					if (0 < commandBuffer.Length) {
						commandBuffer.Remove (commandBuffer.Length-1, 1);
						Status = commandBuffer.ToString ();
						if (0 == commandBuffer.Length)
							Reset (Status);
					}
					break;
				default:
					if(unicodeKey != 0) {
						commandBuffer.Append ((char)unicodeKey);
						Status = commandBuffer.ToString ();
					}
					break;
				}
				return;
				
			case State.WriteChar:
				if (unicodeKey != 0) {
					RunAction (SelectionActions.StartSelection);
					int   roffset = Data.SelectionRange.Offset;
					InsertCharacter ((char) unicodeKey);
					Reset (string.Empty);
					Caret.Offset = roffset;
				} else {
					Reset ("Keystroke was not a character");
				}
				return;
				
			case State.Indent:
				if (((modifier & (ModifierKeys.Control)) == 0 && unicodeKey == '>'))
				{
					RunAction (MiscActions.IndentSelection);
					Reset ("");
					return;
				}
				
				action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
				if (action == null)
					action = ViActionMaps.GetDirectionKeyAction (key, modifier);
				
				if (action != null) {
					RunActions (SelectionActions.FromMoveAction (action), MiscActions.IndentSelection);
					Reset ("");
				} else {
					Reset ("Unrecognised motion");
				}
				return;
				
			case State.Unindent:
				if (((modifier & (ModifierKeys.Control)) == 0 && ((char)unicodeKey) == '<'))
				{
					RunAction (MiscActions.RemoveIndentSelection);
					Reset ("");
					return;
				}
				
				action = ViActionMaps.GetNavCharAction ((char)unicodeKey);
				if (action == null)
					action = ViActionMaps.GetDirectionKeyAction (key, modifier);
				
				if (action != null) {
					RunActions (SelectionActions.FromMoveAction (action), MiscActions.RemoveIndentSelection);
					Reset ("");
				} else {
					Reset ("Unrecognised motion");
				}
				return;

			case State.G:
				if (((modifier & (ModifierKeys.Control)) == 0)) {
					switch ((char)unicodeKey) {
					case 'g':
						Caret.Offset = 0;
						Reset ("");
						return;
					}
				}
				Reset ("Unknown command");
				return;
				
			case State.Mark: {
				char k = (char)unicodeKey;
				ViMark mark = null;
				if (!char.IsLetterOrDigit(k)) {
					Reset ("Invalid Mark");
					return;
				}
				if (marks.ContainsKey(k)) {
					mark = marks [k];
				} else {
					mark = new ViMark(k);
					marks [k] = mark;
				}
				RunAction(mark.SaveMark);
				Reset("");
				return;
			}
			
			case State.NameMacro: {
				char k = (char) unicodeKey;
				if(!char.IsLetterOrDigit(k)) {
					Reset("Invalid Macro Name");
					return;
				}
				currentMacro = new ViMacro (k);
				currentMacro.KeysPressed = new Queue<ViMacro.KeySet> ();
				macros [k] = currentMacro;
				Reset("");
				return;
			}
			
			case State.PlayMacro: {
				char k = (char) unicodeKey;
				if (k == '@') 
					k = macros_lastplayed;
				if (macros.ContainsKey(k)) {
					Reset ("");
					macros_lastplayed = k; // FIXME play nice when playing macros from inside macros?
					ViMacro macroToPlay = macros [k];
					foreach (ViMacro.KeySet keySet in macroToPlay.KeysPressed) {
						HandleKeypress(keySet.Key, keySet.UnicodeKey, keySet.Modifiers); // FIXME stop on errors? essential with multipliers and nowrapscan
					}
					/* Once all the keys have been played back, quickly exit. */
					return;
				} else {
					Reset ("Invalid Macro Name '" + k + "'");
					return;
				}
			}
			
			case State.GoToMark: {
				char k = (char)unicodeKey;
				if (marks.ContainsKey(k)) {
					RunAction(marks [k].LoadMark);
					Reset ("");
				} else {
					Reset ("Unknown Mark");
				}
				return;
			}
				
			case State.Fold:
				if (((modifier & (ModifierKeys.Control)) == 0)) {
					switch ((char)unicodeKey) {
						case 'A':
						// Recursive fold toggle
							action = FoldActions.ToggleFoldRecursive;
							break;
						case 'C':
						// Recursive fold close
							action = FoldActions.CloseFoldRecursive;
							break;
						case 'M':
						// Close all folds
							action = FoldActions.CloseAllFolds;
							break;
						case 'O':
						// Recursive fold open
							action = FoldActions.OpenFoldRecursive;
							break;
						case 'R':
						// Expand all folds
							action = FoldActions.OpenAllFolds;
							break;
						case 'a':
						// Fold toggle
							action = FoldActions.ToggleFold;
							break;
						case 'c':
						// Fold close
							action = FoldActions.CloseFold;
							break;
						case 'o':
						// Fold open
							action = FoldActions.OpenFold;
							break;
						default:
							Reset ("Unknown command");
							break;
					}
					
					if (null != action) {
						RunAction (action);
						Reset (string.Empty);
					}
				}
					
				return;
			}
		}

		static bool IsInnerOrOuterMotionKey (uint unicodeKey, ref Motion motion)
		{
			if (unicodeKey == 'i') {
				motion = Motion.Inner;
				return true;
			} 
			if (unicodeKey == 'a') {
				motion = Motion.Outer;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Runs an in-place replacement on the selection or the current line
		/// using the "pattern", "replacement", and "trailer" groups of match.
		/// </summary>
		public string RegexReplace (System.Text.RegularExpressions.Match match)
		{
			string line = null;
			var segment = TextSegment.Invalid;

			if (Data.IsSomethingSelected) {
				// Operate on selection
				line = Data.SelectedText;
				segment = Data.SelectionRange;
			} else {
				// Operate on current line
				var lineSegment = Data.Document.GetLine (Caret.Line);
				if (lineSegment != null)
					segment = lineSegment;
				line = Data.Document.GetTextBetween (segment.Offset, segment.EndOffset);
			}

			// Set regex options
			RegexOptions options = RegexOptions.Multiline;
			if (match.Groups["trailer"].Success && "i" == match.Groups["trailer"].Value)
				options |= RegexOptions.IgnoreCase;

			// Mogrify group backreferences to .net-style references
			string replacement = Regex.Replace (match.Groups["replacement"].Value, @"\\([0-9]+)", "$$$1", RegexOptions.Compiled);
			replacement = Regex.Replace (replacement, "&", "$$0", RegexOptions.Compiled);

			try {
				string newline = Regex.Replace (line, match.Groups["pattern"].Value, replacement, options);
				Data.Replace (segment.Offset, line.Length, newline);
				if (Data.IsSomethingSelected)
					Data.ClearSelection ();
				lastPattern = match.Groups["pattern"].Value;
				lastReplacement = replacement; 
			} catch (ArgumentException ae) {
				return string.Format("Replacement error: {0}", ae.Message);
			}

			return "Performed replacement.";
		}

		public void ApplyActionToSelection (ModifierKeys modifier, uint unicodeKey)
		{
			if (Data.IsSomethingSelected && (modifier & (ModifierKeys.Control)) == 0) {
				switch ((char)unicodeKey) {
				case 'x':
				case 'd':
					RunAction (ClipboardActions.Cut);
					Reset ("Deleted selection");
					return;
				case 'y':
					int offset = Data.SelectionRange.Offset;
					RunAction (ClipboardActions.Copy);
					Reset ("Yanked selection");
					Caret.Offset = offset;
					return;
				case 's':
				case 'c':
					RunAction (ClipboardActions.Cut);
					Caret.Mode = CaretMode.Insert;
					CurState = State.Insert;
					Status = "-- INSERT --";
					return;
				case 'S':
					Data.SetSelectLines (Data.MainSelection.Anchor.Line, Data.Caret.Line);
					goto case 'c';
					
				case '>':
					RunAction (MiscActions.IndentSelection);
					Reset ("");
					return;
					
				case '<':
					RunAction (MiscActions.RemoveIndentSelection);
					Reset ("");
					return;

				case ':':
					commandBuffer.Append (":");
					Status = commandBuffer.ToString ();
					CurState = State.Command;
					break;
				case 'J':
					RunAction (ViActions.Join);
					Reset ("");
					return;
					
				case '~':
					RunAction (ViActions.ToggleCase);
					Reset ("");
					return;
				}
			}
		}

		private string Search()
		{
			SearchResult result = searchBackward?
				Editor.SearchBackward (Caret.Offset):
				Editor.SearchForward (Caret.Offset+1);
			Editor.HighlightSearchPattern = (null != result);
			if (null == result) 
				return string.Format ("Pattern not found: '{0}'", Editor.SearchPattern);
			else Caret.Offset = result.Offset;
		
			return string.Empty;
		}

		/// <summary>
		/// Pastes the selection after the caret,
		/// or replacing an existing selection.
		/// </summary>
		private void PasteAfter (bool linemode)
		{
			TextEditorData data = Data;
			using (var undo = Document.OpenUndoGroup ()) {
				var contents = Clipboard.GetText ();
				if (contents == null)
						return;
					if (contents.EndsWith ("\r") || contents.EndsWith ("\n")) {
						// Line mode paste
						if (data.IsSomethingSelected) {
							// Replace selection
							RunAction (ClipboardActions.Cut);
							data.InsertAtCaret (data.EolMarker);
							int offset = data.Caret.Offset;
							data.InsertAtCaret (contents);
							if (linemode) {
								// Existing selection was also in line mode
								data.Caret.Offset = offset;
								RunAction (DeleteActions.FromMoveAction (CaretMoveActions.Left));
							}
							RunAction (CaretMoveActions.LineStart);
						} else {
							// Paste on new line
							RunAction (ViActions.NewLineBelow);
							RunAction (DeleteActions.FromMoveAction (CaretMoveActions.LineStart));
							data.InsertAtCaret (contents);
							RunAction (DeleteActions.FromMoveAction (CaretMoveActions.Left));
							RunAction (CaretMoveActions.LineStart);
						}
					} else {
						// Inline paste
						if (data.IsSomethingSelected) 
							RunAction (ClipboardActions.Cut);
						else RunAction (CaretMoveActions.Right);
						data.InsertAtCaret (contents);
						RunAction (ViActions.Left);
					}
					Reset (string.Empty);
			}
		}

		/// <summary>
		/// Pastes the selection before the caret,
		/// or replacing an existing selection.
		/// </summary>
		private void PasteBefore (bool linemode)
		{
			TextEditorData data = Data;
			
			using (var undo = Document.OpenUndoGroup ()) {
				var contents = Clipboard.GetText ();
				if (contents == null)
						return;
					if (contents.EndsWith ("\r") || contents.EndsWith ("\n")) {
						// Line mode paste
						if (data.IsSomethingSelected) {
							// Replace selection
							RunAction (ClipboardActions.Cut);
							data.InsertAtCaret (data.EolMarker);
							int offset = data.Caret.Offset;
							data.InsertAtCaret (contents);
							if (linemode) {
								// Existing selection was also in line mode
								data.Caret.Offset = offset;
								RunAction (DeleteActions.FromMoveAction (CaretMoveActions.Left));
							}
							RunAction (CaretMoveActions.LineStart);
						} else {
							// Paste on new line
							RunAction (ViActions.NewLineAbove);
							RunAction (DeleteActions.FromMoveAction (CaretMoveActions.LineStart));
							data.InsertAtCaret (contents);
							RunAction (DeleteActions.FromMoveAction (CaretMoveActions.Left));
							RunAction (CaretMoveActions.LineStart);
						}
					} else {
						// Inline paste
						if (data.IsSomethingSelected) 
							RunAction (ClipboardActions.Cut);
						data.InsertAtCaret (contents);
						RunAction (ViActions.Left);
					}
					Reset (string.Empty);
			}
		}

		enum State {
			Unknown = 0,
			Normal,
			Command,
			Delete,
			Yank,
			Visual,
			VisualLine,
			Insert,
			Replace,
			WriteChar,
			Change,
			Indent,
			Unindent,
			G,
			Fold,
			Mark,
			GoToMark,
			NameMacro,
			PlayMacro
		}
	}

	public enum Motion {
		None = 0,
		Inner,
		Outer
	}
}