﻿using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using PataNext.Export.Desktop.Visual.Dependencies;

namespace PataNext.Export.Desktop.Visual
{
	public class HomeChangelogScreen : Screen
	{
		public readonly HomeChangelogControl Control;

		public HomeChangelogScreen()
		{
			Masking      = true;
			CornerRadius = 8;

			AddRangeInternal(new Drawable[]
			{
				new Box {Size = new(1), RelativeSizeAxes     = Axes.Both, Colour = Colour4.Black.Opacity(0.5f)},
				new Box {Size = new(1, 60), RelativeSizeAxes = Axes.X, Colour    = Colour4.Black.Opacity(0.6f)},
			});

			AddRangeInternal(new Drawable[]
			{
				new FillFlowContainer
				{
					Size             = new(1),
					RelativeSizeAxes = Axes.Both,

					// Changelog header
					Children = new Drawable[]
					{
						new Container
						{
							Size             = new(1, 50),
							RelativeSizeAxes = Axes.X,

							Children = new Drawable[]
							{
								new Box {Size = new(1), RelativeSizeAxes = Axes.Both, Colour = Colour4.FromRGBA(0xffedd2ff)},
								new SpriteText
								{
									Origin = Anchor.Centre, Anchor = Anchor.Centre,

									Text   = "Changelog",
									Font   = new("mojipon"),
									Colour = Colour4.FromRGBA(0x25110aff)
								}
							}
						},
						new Container
						{
							Padding  = new() {Horizontal = 40, Top = 11},
							Masking = true,

							Size             = new(1, 1),
							RelativeSizeAxes = Axes.Both,

							Child = Control = new()
							{
								Size             = new(1, 1),
								RelativeSizeAxes = Axes.Both,
							}
						}
					}
				}
			});
		}

		private ICurrentVersion    currentVersion;
		private IChangelogProvider changelogProvider;

		private void onChangelogChange(ValueChangedEvent<Dictionary<string, string[]>> ev)
		{
			if (ev.NewValue == null)
			{
				Control.Set(currentVersion.Current.Value, new());
				return;
			}

			Control.Set(currentVersion.Current.Value, ev.NewValue);
		}

		[BackgroundDependencyLoader]
		private void load(ICurrentVersion currentVersion, IChangelogProvider changelogProvider)
		{
			this.currentVersion    = currentVersion;
			this.changelogProvider = changelogProvider;
			
			changelogProvider.Current.BindValueChanged(onChangelogChange, true);
		}

		protected override void Dispose(bool isDisposing)
		{
			changelogProvider.Current.ValueChanged -= onChangelogChange;
			
			base.Dispose(isDisposing);
		}
	}
}