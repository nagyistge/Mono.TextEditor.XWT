// BookmarkMargin.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (c) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using Xwt;
using Xwt.Drawing;

namespace Mono.TextEditor
{
	public class IconMargin : Margin
	{
		TextEditor editor;
		Color backgroundColor, separatorColor;
		TextLayout layout;
		double marginWidth = 18;
		
		public IconMargin (TextEditor editor)
		{
			this.editor = editor;
			layout = new TextLayout (editor.TextArea);
		}
		
		public override double Width {
			get {
				return marginWidth;
			}
		}
		
		public override void Dispose ()
		{
			layout = layout.Kill ();
		}
		
		internal protected override void OptionsChanged ()
		{
			backgroundColor = editor.ColorStyle.IndicatorMargin.Color;
			separatorColor = editor.ColorStyle.IndicatorMarginSeparator.Color;
			layout.Font = editor.Options.Font;
			layout.Text = "!";
			marginWidth = layout.GetSize ().Width;
			//layout.GetPixelSize (out tmp, out this.marginWidth);
			marginWidth *= 12;
			marginWidth /= 10;
		}
		
		internal protected override void MousePressed (MarginMouseEventArgs args)
		{
			base.MousePressed (args);
			
			DocumentLine lineSegment = args.LineSegment;
			if (lineSegment != null) {
				foreach (TextLineMarker marker in lineSegment.Markers) {
					var marginMarker = marker as MarginMarker;
					if (marginMarker != null) 
						marginMarker.InformMousePress (editor, this, args);

					if (marker is IIconBarMarker) 
						((IIconBarMarker)marker).MousePress (args);
				}
			}
		}
		
		internal protected override void MouseReleased (MarginMouseEventArgs args)
		{
			base.MouseReleased (args);
			
			DocumentLine lineSegment = args.LineSegment;
			if (lineSegment != null) {
				foreach (TextLineMarker marker in lineSegment.Markers) {
					var marginMarker = marker as MarginMarker;
					if (marginMarker != null) 
						marginMarker.InformMouseRelease (editor, this, args);

					if (marker is IIconBarMarker) 
						((IIconBarMarker)marker).MouseRelease (args);
				}
			}
		}

		internal protected override void MouseHover (MarginMouseEventArgs args)
		{
			base.MouseHover (args);
			args.Editor.TooltipText = null;
			DocumentLine lineSegment = args.LineSegment;
			if (lineSegment != null) {
				foreach (TextLineMarker marker in lineSegment.Markers) {
					var marginMarker = marker as MarginMarker;
					if (marginMarker != null) 
						marginMarker.InformMouseHover (editor, this, args);
					if (marker is IIconBarMarker) 
						((IIconBarMarker)marker).MouseHover (args);
				}
			}
		}

		internal protected override void Draw (Context ctx, Rectangle area, DocumentLine lineSegment, int line, double x, double y, double lineHeight)
		{
			bool backgroundIsDrawn = false;
			if (lineSegment != null) {
				foreach (var marker in lineSegment.Markers) {
					var marginMarker = marker as MarginMarker;
					if (marginMarker != null && marginMarker.CanDrawBackground (this)) {
						backgroundIsDrawn = marginMarker.DrawBackground (editor, ctx, new MarginDrawMetrics (this, area, lineSegment, line, x, y, lineHeight));
					}

					var iconMarker = marker as IIconBarMarker;
					if (iconMarker == null || !iconMarker.CanDrawBackground)
						continue;
					iconMarker.DrawBackground (editor, ctx, lineSegment, line, x, y, (int)Width, editor.LineHeight);
					backgroundIsDrawn = true;
					break;
				}
			}

			if (!backgroundIsDrawn) {
				ctx.Rectangle (x, y, Width, lineHeight);
				ctx.SetColor(backgroundColor);
				ctx.Fill ();
				
				ctx.MoveTo (x + Width - 0.5, y);
				ctx.LineTo (x + Width - 0.5, y + lineHeight);
				ctx.SetColor(separatorColor);
				ctx.Stroke ();
			}

			if (lineSegment != null && line <= editor.Document.LineCount) {
				foreach (var marker in lineSegment.Markers) {
					var marginMarker = marker as MarginMarker;
					if (marginMarker != null && marginMarker.CanDrawForeground (this)) {
						marginMarker.DrawForeground (editor, ctx, new MarginDrawMetrics (this, area, lineSegment, line, x, y, lineHeight));
					}

					if (marker is IIconBarMarker) 
						((IIconBarMarker)marker).DrawIcon (editor, ctx, lineSegment, line, x, y, (int)Width, editor.LineHeight);
				}
				if (DrawEvent != null) 
					DrawEvent (this, new BookmarkMarginDrawEventArgs (editor, ctx, lineSegment, line, x, y));
			}
		}
		
		public EventHandler<BookmarkMarginDrawEventArgs> DrawEvent;
	}
	
	public class BookmarkMarginDrawEventArgs : EventArgs
	{
		public TextEditor Editor {
			get;
			private set;
		}

		public Context Context {
			get;
			private set;
		}

		public int Line {
			get;
			private set;
		}

		public double X {
			get;
			private set;
		}

		public double Y {
			get;
			private set;
		}

		public DocumentLine LineSegment {
			get;
			private set;
		}
		
		public BookmarkMarginDrawEventArgs (TextEditor editor, Context context, DocumentLine line, int lineNumber, double xPos, double yPos)
		{
			this.Editor = editor;
			this.Context    = context;
			this.LineSegment = line;
			this.Line   = lineNumber;
			this.X      = xPos;
			this.Y      = yPos;
		}
	}
	
}
