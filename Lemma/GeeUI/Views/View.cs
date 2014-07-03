﻿using System;
using System.Linq;
using System.Collections.Generic;
using ComponentBind;
using Lemma;
using Lemma.Components;
using Lemma.GeeUI.Composites;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using GeeUI.Structs;
using GeeUI.ViewLayouts;
using Newtonsoft.Json.Schema;
using System.Collections.ObjectModel;

namespace GeeUI.Views
{
	public class View : Component<Main>
	{
		public delegate void MouseClickEventHandler(object sender, EventArgs e);
		public delegate void MouseOverEventHandler(object sender, EventArgs e);
		public delegate void MouseOffEventHandler(object sender, EventArgs e);
		public delegate void MouseScrollEventHandler(int delta);

		public event MouseClickEventHandler OnMouseClick;
		public event MouseClickEventHandler OnMouseRightClick;
		public event MouseClickEventHandler OnMouseClickAway;
		public event MouseOverEventHandler OnMouseOver;
		public event MouseOffEventHandler OnMouseOff;
		public event MouseScrollEventHandler OnMouseScroll;

		public GeeUIMain ParentGeeUI;
		public Property<View> ParentView = new Property<View>();

		public List<ViewLayout> ChildrenLayouts = new List<ViewLayout>();

		public Property<bool> IgnoreParentBounds = new Property<bool>() { Value = true };
		public Property<bool> Selected = new Property<bool>() { Value = false };
		public new Property<bool> Active = new Property<bool>() { Value = true };
		public Property<bool> EnabledScissor = new Property<bool>() { Value = true };
		public Property<bool> ContentMustBeScissored = new Property<bool>() { Value = false };

		public Property<bool> AllowMouseEvents = new Property<bool>() { Value = true };

		public Property<bool> EnforceRootAttachment = new Property<bool>() { Value = true };

		public ToolTip ToolTipView;
		private Property<string> _toolTipText = new Property<string>();
		private SpriteFont _toolTipFont;
		private float _toolTipTimer;

		public Property<float> MyOpacity = new Property<float> { Value = 1f };

		public Property<bool> Attached = new Property<bool>();

		public float EffectiveOpacity
		{
			get
			{
				if (ParentView.Value == null) return MyOpacity;
				return MyOpacity * ParentView.Value.EffectiveOpacity;
			}
		}

		public Property<int> NumChildrenAllowed = new Property<int>() { Value = -1 };

		public string Name;

		protected bool _mouseOver;
		
		public bool MouseOver
		{
			get
			{
				return _mouseOver;
			}
			set
			{
				_mouseOver = value;
				if (value)
					OnMOver();
				else
					OnMOff();
			}
		}

		public virtual Rectangle BoundBox
		{
			get
			{
				return new Rectangle(RealX, RealY, Width, Height);
			}
		}

		public virtual Rectangle AbsoluteBoundBox
		{
			get
			{
				if (ParentView.Value == null) return BoundBox;
				Rectangle curBB = BoundBox;
				return new Rectangle(AbsoluteX, AbsoluteY, curBB.Width, curBB.Height);
			}
		}

		public virtual Rectangle ContentBoundBox
		{
			get
			{
				return BoundBox;
			}
		}

		public virtual Rectangle AbsoluteContentBoundBox
		{
			get
			{
				if (ParentView.Value == null) return ContentBoundBox;
				Rectangle curBB = ContentBoundBox;
				curBB.X += AbsoluteX - RealX;
				curBB.Y += AbsoluteY - RealY;
				return curBB;
			}
		}

		public int X
		{
			get
			{
				return (int)Position.Value.X;
			}
			set
			{
				Position.Value = new Vector2(value, Y);
			}
		}
		public int Y
		{
			get
			{
				return (int)Position.Value.Y;
			}
			set
			{
				Position.Value = new Vector2(X, value);
			}
		}

		public int RealX
		{
			get
			{
				if (ParentView.Value == null) return X - (int)AnchorOffset.X;
				return X - (int)ParentView.Value.ContentOffset.Value.X - (int)AnchorOffset.X;
			}
		}

		public int RealY
		{
			get
			{
				if (ParentView.Value == null) return Y - (int)AnchorOffset.Y;
				return Y - (int)ParentView.Value.ContentOffset.Value.Y - (int)AnchorOffset.Y;
			}
		}

		public Property<Vector2> Position = new Property<Vector2>() { Value = Vector2.Zero };

		public Vector2 AnchorOffset
		{
			get
			{
				return new Vector2((float)Width * AnchorPoint.Value.X, (float)Height * AnchorPoint.Value.Y);
			}
		}
		public Vector2 RealPosition
		{
			get
			{
				return new Vector2(RealX, RealY);
			}
		}

		public Property<Vector2> ContentOffset = new Property<Vector2>() { Value = Vector2.Zero };

		public Property<Vector2> AnchorPoint = new Property<Vector2>() { Value = Vector2.Zero };

		public int AbsoluteX
		{
			get
			{
				return (int)AbsolutePosition.X;
			}
		}
		public int AbsoluteY
		{
			get
			{
				return (int)AbsolutePosition.Y;
			}
		}
		public Vector2 AbsolutePosition
		{
			get
			{
				if (ParentView.Value == null) return RealPosition;
				return RealPosition + ParentView.Value.AbsolutePosition;
			}
		}

		public Property<int> Width = new Property<int>() { Value = 0 };
		public Property<int> Height = new Property<int>() { Value = 0 };

		public ListProperty<View> Children = new ListProperty<View>();

		internal View(GeeUIMain theGeeUI)
		{
			ParentGeeUI = theGeeUI;

			this.Attached.Set = delegate(bool value)
			{
				this.Attached.InternalValue = value;
				foreach (View v in this.Children)
					v.Attached.Value = value;
			};

			this.ParentView.Set = delegate(View v)
			{
				this.ParentView.InternalValue = v;
				this.Attached.Value = v == null ? false : v.Attached;
			};

			this.Add(new NotifyBinding(delegate()
			{
				View parent = this.ParentView;
				if (parent != null)
					parent.dirty = true;
			}, this.Position, this.Active, this.Width, this.Height));
		}

		public View(GeeUIMain theGeeUI, View parentView)
			: this(theGeeUI)
		{
			if (parentView != null)
				parentView.AddChild(this);
		}

		#region Child management

		public virtual void AddChild(View child)
		{
			if (child == null) return;
			if (Children.Count + 1 > NumChildrenAllowed && NumChildrenAllowed != -1)
				throw new Exception("You have attempted to add too many child Views to this View.");
			//Ensure that a child can only belong to one View ever.
			if (child.ParentView.Value != null)
				child.ParentView.Value.RemoveChild(child);
			child.ParentView.Value = this;
			child.ParentGeeUI = ParentGeeUI;
			Children.Add(child);
			this.dirty = true;
		}

		public void RemoveAllChildren()
		{
			foreach (var child in Children)
				child.ParentView.Value = null;
			Children.Clear();
			this.dirty = true;
		}

		public void RemoveChild(View child)
		{
			Children.Remove(child);
			child.ParentView.Value = null;
			this.dirty = true;
		}

		public void OrderChildren()
		{
			foreach (var layout in ChildrenLayouts)
				layout.OrderChildren(this);
			this.dirty = false;
		}
		
		public List<View> FindChildrenByName(string name, int depth = -1, List<View> list = null)
		{
			if (list == null)
				list = new List<View>();
			bool infinite = depth == -1;
			if (!infinite) depth--;
			if (depth >= 0 || infinite)
			{
				foreach (var c in Children)
				{
					if (c.Name == name)
						list.Add(c);
					c.FindChildrenByName(name, infinite ? -1 : depth, list);
				}
			}
			return list;
		}

		public View FindFirstChildByName(string name, int depth = -1)
		{
			bool infinite = depth == -1;
			if (!infinite) depth--;

			if (depth >= 0 || infinite)
			{
				foreach (var c in Children)
				{
					if (c.Name == name)
						return c;
					foreach (var find in c.FindChildrenByName(name, infinite ? -1 : depth))
						return find;
				}
			}
			return null;
		}

		public void RemoveToolTip()
		{
			_toolTipTimer = 0f;
			if (this.ToolTipView != null)
			{
				this.ToolTipView.ParentView.Value.RemoveChild(ToolTipView);
				this.ToolTipView.OnDelete();
				this.ToolTipView = null;
			}
		}

		public void SetToolTipText(string text, SpriteFont font)
		{
			if (text == null) return;
			this._toolTipText.Value = text;
			this._toolTipFont = font;
		}

		private void ShowToolTip()
		{
			RemoveToolTip();
			this.ToolTipView = new ToolTip(ParentGeeUI, ParentGeeUI.RootView, this, this._toolTipText, _toolTipFont);
		}

		#endregion

		#region Setters

		public View SetWidth(int width)
		{
			this.Width.Value = width;
			return this;
		}

		public View SetHeight(int height)
		{
			this.Height.Value = height;
			return this;
		}

		public View SetPosition(Vector2 position)
		{
			this.Position.Value = position;
			return this;
		}

		public View SetOpacity(float opacity)
		{
			this.MyOpacity.Value = opacity;
			return this;
		}

		public View SetContentOffset(Vector2 offset)
		{
			this.ContentOffset.Value = offset;
			return this;
		}

		#endregion

		#region Parent management

		public void SetParent(View parent)
		{
			if (ParentView.Value != null)
				ParentView.Value.RemoveChild(this);
			parent.AddChild(this);
		}

		#endregion

		#region Child depth ordering

		public virtual void BringChildToFront(View view)
		{
			if (Children[Children.Count - 1] != view)
			{
				Children.Remove(view);
				Children.Add(view);
				this.dirty = true;
			}
		}

		#endregion

		public void ResetOnMouseClick()
		{
			OnMouseClick = null;
		}

		public void ResetOnMouseScroll()
		{
			OnMouseScroll = null;
		}

		#region Virtual methods/events

		public virtual void OnDelete()
		{
			Active.Value = false;
			foreach (var child in Children)
				child.OnDelete();
			this.delete();
		}

		public virtual void OnMScroll(Vector2 position, int scrollDelta, bool fromChild = false)
		{
			if (ParentView.Value != null) ParentView.Value.OnMScroll(position, scrollDelta, true);
			if (OnMouseScroll != null)
				OnMouseScroll(scrollDelta);
		}

		public virtual void OnMRightClick(Vector2 position, bool fromChild = false)
		{
			if (OnMouseRightClick != null)
				OnMouseRightClick(this, new EventArgs());
			if (ParentView.Value != null) ParentView.Value.OnMRightClick(position, true);
		}

		public virtual void OnMClick(Vector2 position, bool fromChild = false)
		{
			if (OnMouseClick != null)
				OnMouseClick(this, new EventArgs());
			if (ParentView.Value != null) ParentView.Value.OnMClick(position, true);
		}

		public virtual void OnMClickAway(bool fromChild = false)
		{
			if (OnMouseClickAway != null)
				OnMouseClickAway(this, new EventArgs());
			if (ParentView.Value != null) ParentView.Value.OnMClickAway(true);
		}

		public virtual void OnMOver(bool fromChild = false)
		{
			RemoveToolTip();
			if (OnMouseOver != null)
				OnMouseOver(this, new EventArgs());
			if (ParentView.Value != null) ParentView.Value.OnMOver(true);
		}

		public virtual void OnMOff(bool fromChild = false)
		{
			RemoveToolTip();
			if (OnMouseOff != null)
				OnMouseOff(this, new EventArgs());
			if (ParentView.Value != null) ParentView.Value.OnMOff(true);
		}

		protected bool dirty;

		public virtual void Update(float dt)
		{
			if (MouseOver && !string.IsNullOrEmpty(_toolTipText.Value))
			{
				_toolTipTimer += dt;
				if (_toolTipTimer >= 1f && _toolTipFont != null && ToolTipView == null)
					ShowToolTip();
			}

			if (ParentView.Value == null || IgnoreParentBounds)
				return;

			var curBB = AbsoluteBoundBox;
			var parentBB = ParentView.Value.AbsoluteContentBoundBox;
			var xOffset = curBB.Right - parentBB.Right;
			var yOffset = curBB.Bottom - parentBB.Bottom;
			if (xOffset > 0)
				X -= xOffset;
			else
			{
				xOffset = curBB.Left - parentBB.Left;
				if (xOffset < 0)
					X -= xOffset;
			}
			if (yOffset > 0)
				Y -= yOffset;
			else
			{
				yOffset = curBB.Top - parentBB.Top;
				if (yOffset < 0)
					Y -= yOffset;
			}
		}

		public void PostUpdate()
		{
			if (this.dirty)
			{
				foreach (var layout in ChildrenLayouts)
					layout.OrderChildren(this);
				this.dirty = false;
			}
		}

		public virtual void Draw(SpriteBatch spriteBatch)
		{
		}

		/// <summary>
		/// This will essentially cause the view to draw the things that should be scissored to its own bounds.
		/// </summary>
		/// <param name="spriteBatch"></param>
		public virtual void DrawContent(SpriteBatch spriteBatch)
		{

		}

		#endregion
	}
}
