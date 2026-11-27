using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed class AppThemePalette
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsDark { get; }
    public bool IsDefault { get; }

    public Color SurfaceColor { get; }
    public Windows.UI.Color SurfaceCompositionColor { get; }
    public Color PreviewCardColor { get; }
    public Color PreviewAccentColor { get; }

    public SolidColorBrush Surface { get; }
    public SolidColorBrush HitSurface { get; }
    public SolidColorBrush Text { get; }
    public SolidColorBrush Muted { get; }
    public string MutedHex { get; }
    public SolidColorBrush Field { get; }
    public SolidColorBrush Card { get; }
    public SolidColorBrush CardSolid { get; }
    public SolidColorBrush StagePanel { get; }
    public SolidColorBrush Line { get; }
    public SolidColorBrush Accent { get; }
    public SolidColorBrush AccentHover { get; }
    public SolidColorBrush AccentPressed { get; }
    public SolidColorBrush AccentBorder { get; }
    public SolidColorBrush AccentHoverBorder { get; }
    public SolidColorBrush AccentText { get; }
    public SolidColorBrush ButtonSurface { get; }
    public SolidColorBrush ButtonHover { get; }
    public SolidColorBrush ButtonPressed { get; }
    public SolidColorBrush ButtonBorder { get; }
    public SolidColorBrush ButtonText { get; }
    public SolidColorBrush CloseButtonSurface { get; }
    public SolidColorBrush CloseButtonHover { get; }
    public SolidColorBrush CloseButtonPressed { get; }
    public SolidColorBrush CloseButtonBorder { get; }
    public SolidColorBrush CloseButtonText { get; }
    public SolidColorBrush CheckSurface { get; }
    public SolidColorBrush CheckHoverSurface { get; }
    public SolidColorBrush CheckBorder { get; }
    public SolidColorBrush CheckCheckedFill { get; }
    public SolidColorBrush CheckMark { get; }
    public SolidColorBrush InputBorder { get; }
    public SolidColorBrush Selection { get; }
    public SolidColorBrush SliderTrack { get; }
    public SolidColorBrush ScrollThumb { get; }
    public SolidColorBrush ComboArrow { get; }
    public SolidColorBrush MenuSurface { get; }
    public SolidColorBrush MenuHighlight { get; }
    public SolidColorBrush ListHover { get; }
    public SolidColorBrush NavHover { get; }
    public SolidColorBrush NavPressed { get; }
    public SolidColorBrush NavSelected { get; }
    public SolidColorBrush NavSelectedBorder { get; }
    public SolidColorBrush DashedStroke { get; }
    public SolidColorBrush DashedHover { get; }
    public SolidColorBrush DashedPressed { get; }
    public SolidColorBrush PillBackground { get; }
    public SolidColorBrush HintBackground { get; }
    public SolidColorBrush CountBubbleBackground { get; }
    public SolidColorBrush BubbleSurface { get; }
    public SolidColorBrush BubbleHover { get; }
    public SolidColorBrush ReorderGlyph { get; }
    public SolidColorBrush PinIdleFill { get; }
    public SolidColorBrush PinIdleStroke { get; }
    public SolidColorBrush PinPinnedSurface { get; }
    public SolidColorBrush PinPinnedHoverSurface { get; }
    public SolidColorBrush PinPinnedBorder { get; }
    public SolidColorBrush PinPinnedHoverBorder { get; }
    public SolidColorBrush PinPinnedFill { get; }
    public SolidColorBrush PinPinnedStroke { get; }
    public SolidColorBrush PlatformTabActive { get; }
    public SolidColorBrush PlatformTabIdle { get; }
    public SolidColorBrush PlatformDivider { get; }
    public SolidColorBrush Danger { get; }
    public SolidColorBrush Warn { get; }
    public SolidColorBrush Attention { get; }
    public SolidColorBrush DisabledIcon { get; }

    public System.Drawing.Color MenuBackDrawingColor { get; }
    public System.Drawing.Color MenuTextDrawingColor { get; }
    public System.Drawing.Color MenuDisabledDrawingColor { get; }
    public System.Drawing.Color MenuHighlightDrawingColor { get; }
    public System.Drawing.Color MenuBorderDrawingColor { get; }
    public System.Drawing.Color SwitchTrackOnDrawingColor { get; }
    public System.Drawing.Color SwitchTrackOffDrawingColor { get; }
    public System.Drawing.Color SwitchDotDrawingColor { get; }

    internal AppThemePalette(
        string id,
        string name,
        string description,
        bool isDark,
        bool isDefault,
        string surface,
        string text,
        string muted,
        string field,
        string card,
        string cardSolid,
        string stagePanel,
        string line,
        string accent,
        string accentHover,
        string accentPressed,
        string accentBorder,
        string accentHoverBorder,
        string accentText,
        string buttonSurface,
        string buttonHover,
        string buttonPressed,
        string buttonBorder,
        string buttonText,
        string closeButtonSurface,
        string closeButtonHover,
        string closeButtonPressed,
        string closeButtonBorder,
        string closeButtonText,
        string checkSurface,
        string checkHoverSurface,
        string checkBorder,
        string checkCheckedFill,
        string checkMark,
        string inputBorder,
        string selection,
        string sliderTrack,
        string scrollThumb,
        string comboArrow,
        string menuSurface,
        string menuHighlight,
        string listHover,
        string navHover,
        string navPressed,
        string navSelected,
        string navSelectedBorder,
        string dashedStroke,
        string dashedHover,
        string dashedPressed,
        string pillBackground,
        string hintBackground,
        string countBubbleBackground,
        string bubbleSurface,
        string bubbleHover,
        string reorderGlyph,
        string pinIdleFill,
        string pinIdleStroke,
        string pinPinnedSurface,
        string pinPinnedHoverSurface,
        string pinPinnedBorder,
        string pinPinnedHoverBorder,
        string pinPinnedFill,
        string pinPinnedStroke,
        string platformTabActive,
        string platformTabIdle,
        string platformDivider,
        string danger,
        string warn,
        string attention,
        string disabledIcon)
    {
        Id = id;
        Name = name;
        Description = description;
        IsDark = isDark;
        IsDefault = isDefault;

        SurfaceColor = ParseColor(surface);
        SurfaceCompositionColor = Windows.UI.Color.FromArgb(255, SurfaceColor.R, SurfaceColor.G, SurfaceColor.B);
        PreviewCardColor = ParseColor(cardSolid);
        PreviewAccentColor = ParseColor(accent);

        Surface = FreezeBrush(SurfaceColor);
        HitSurface = FreezeBrush(Color.FromArgb(1, SurfaceColor.R, SurfaceColor.G, SurfaceColor.B));
        Text = FreezeBrush(text);
        Muted = FreezeBrush(muted);
        MutedHex = muted;
        Field = FreezeBrush(field);
        Card = FreezeBrush(card);
        CardSolid = FreezeBrush(cardSolid);
        StagePanel = FreezeBrush(stagePanel);
        Line = FreezeBrush(line);
        Accent = FreezeBrush(accent);
        AccentHover = FreezeBrush(accentHover);
        AccentPressed = FreezeBrush(accentPressed);
        AccentBorder = FreezeBrush(accentBorder);
        AccentHoverBorder = FreezeBrush(accentHoverBorder);
        AccentText = FreezeBrush(accentText);
        ButtonSurface = FreezeBrush(buttonSurface);
        ButtonHover = FreezeBrush(buttonHover);
        ButtonPressed = FreezeBrush(buttonPressed);
        ButtonBorder = FreezeBrush(buttonBorder);
        ButtonText = FreezeBrush(buttonText);
        CloseButtonSurface = FreezeBrush(closeButtonSurface);
        CloseButtonHover = FreezeBrush(closeButtonHover);
        CloseButtonPressed = FreezeBrush(closeButtonPressed);
        CloseButtonBorder = FreezeBrush(closeButtonBorder);
        CloseButtonText = FreezeBrush(closeButtonText);
        CheckSurface = FreezeBrush(checkSurface);
        CheckHoverSurface = FreezeBrush(checkHoverSurface);
        CheckBorder = FreezeBrush(checkBorder);
        CheckCheckedFill = FreezeBrush(checkCheckedFill);
        CheckMark = FreezeBrush(checkMark);
        InputBorder = FreezeBrush(inputBorder);
        Selection = FreezeBrush(selection);
        SliderTrack = FreezeBrush(sliderTrack);
        ScrollThumb = FreezeBrush(scrollThumb);
        ComboArrow = FreezeBrush(comboArrow);
        MenuSurface = FreezeBrush(menuSurface);
        MenuHighlight = FreezeBrush(menuHighlight);
        ListHover = FreezeBrush(listHover);
        NavHover = FreezeBrush(navHover);
        NavPressed = FreezeBrush(navPressed);
        NavSelected = FreezeBrush(navSelected);
        NavSelectedBorder = FreezeBrush(navSelectedBorder);
        DashedStroke = FreezeBrush(dashedStroke);
        DashedHover = FreezeBrush(dashedHover);
        DashedPressed = FreezeBrush(dashedPressed);
        PillBackground = FreezeBrush(pillBackground);
        HintBackground = FreezeBrush(hintBackground);
        CountBubbleBackground = FreezeBrush(countBubbleBackground);
        BubbleSurface = FreezeBrush(bubbleSurface);
        BubbleHover = FreezeBrush(bubbleHover);
        ReorderGlyph = FreezeBrush(reorderGlyph);
        PinIdleFill = FreezeBrush(pinIdleFill);
        PinIdleStroke = FreezeBrush(pinIdleStroke);
        PinPinnedSurface = FreezeBrush(pinPinnedSurface);
        PinPinnedHoverSurface = FreezeBrush(pinPinnedHoverSurface);
        PinPinnedBorder = FreezeBrush(pinPinnedBorder);
        PinPinnedHoverBorder = FreezeBrush(pinPinnedHoverBorder);
        PinPinnedFill = FreezeBrush(pinPinnedFill);
        PinPinnedStroke = FreezeBrush(pinPinnedStroke);
        PlatformTabActive = FreezeBrush(platformTabActive);
        PlatformTabIdle = FreezeBrush(platformTabIdle);
        PlatformDivider = FreezeBrush(platformDivider);
        Danger = FreezeBrush(danger);
        Warn = FreezeBrush(warn);
        Attention = FreezeBrush(attention);
        DisabledIcon = FreezeBrush(disabledIcon);

        var menuBg = ToDrawingOpaque(MenuSurface.Color, SurfaceColor);
        MenuBackDrawingColor = menuBg;
        MenuTextDrawingColor = ToDrawingOpaque(Text.Color, SurfaceColor);
        MenuDisabledDrawingColor = ToDrawingOpaque(Muted.Color, SurfaceColor);
        MenuHighlightDrawingColor = ToDrawingOpaque(MenuHighlight.Color, SurfaceColor);
        MenuBorderDrawingColor = ToDrawingOpaque(Line.Color, SurfaceColor);
        SwitchTrackOnDrawingColor = ToDrawingOpaque(isDark ? CheckCheckedFill.Color : Accent.Color, SurfaceColor);
        SwitchTrackOffDrawingColor = ToDrawingOpaque(SliderTrack.Color, SurfaceColor);
        SwitchDotDrawingColor = System.Drawing.Color.White;
    }

    internal SolidColorBrush SurfaceWithAlpha(byte alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, SurfaceColor.R, SurfaceColor.G, SurfaceColor.B));
        b.Freeze();
        return b;
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush FreezeBrush(string hex) => FreezeBrush(ParseColor(hex));

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private static System.Drawing.Color ToDrawingOpaque(Color fg, Color bg)
    {
        var c = ThemePalette.CompositeOver(fg, bg);
        return System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
    }
}

internal static class ThemePalette
{
    internal static readonly AppThemePalette MidnightCyan = new(
        id: "midnight-cyan",
        name: "深夜青蓝",
        description: "经典青绿点缀配深蓝玻璃，柔和护眼",
        isDark: true,
        isDefault: true,
        surface: "#192333",
        text: "#EBEEF6",
        muted: "#9CAAC2",
        field: "#121D2D",
        card: "#C0243044",
        cardSolid: "#243044",
        stagePanel: "#A0121D2D",
        line: "#3A485E",
        accent: "#96DACC",
        accentHover: "#AFE7DC",
        accentPressed: "#7EC8B9",
        accentBorder: "#BCEFE5",
        accentHoverBorder: "#D8FAF3",
        accentText: "#0D2320",
        buttonSurface: "#E8334662",
        buttonHover: "#F241597A",
        buttonPressed: "#E8293A52",
        buttonBorder: "#5E789B",
        buttonText: "#F3F7FD",
        closeButtonSurface: "#DC4F6584",
        closeButtonHover: "#EB627CA1",
        closeButtonPressed: "#DC3E516B",
        closeButtonBorder: "#FFFFFF",
        closeButtonText: "#FFFFFF",
        checkSurface: "#263954",
        checkHoverSurface: "#324A6B",
        checkBorder: "#7A96BC",
        checkCheckedFill: "#2B5A52",
        checkMark: "#E8FFF9",
        inputBorder: "#567093",
        selection: "#527A83",
        sliderTrack: "#587194",
        scrollThumb: "#A8D6C9",
        comboArrow: "#DCE7F7",
        menuSurface: "#EE1E2A3C",
        menuHighlight: "#395565",
        listHover: "#303F55",
        navHover: "#543D5370",
        navPressed: "#70344862",
        navSelected: "#36574F",
        navSelectedBorder: "#668B80",
        dashedStroke: "#AFC2DC",
        dashedHover: "#3A425B7A",
        dashedPressed: "#4D334863",
        pillBackground: "#13202F",
        hintBackground: "#F0243044",
        countBubbleBackground: "#EA1C2A3E",
        bubbleSurface: "#334866",
        bubbleHover: "#415B7E",
        reorderGlyph: "#C4CEDC",
        pinIdleFill: "#66AFC3DE",
        pinIdleStroke: "#D5E0F0",
        pinPinnedSurface: "#E62D534B",
        pinPinnedHoverSurface: "#F236665C",
        pinPinnedBorder: "#96DACC",
        pinPinnedHoverBorder: "#B5EFE3",
        pinPinnedFill: "#96DACC",
        pinPinnedStroke: "#C6F4EB",
        platformTabActive: "#595184B9",
        platformTabIdle: "#202C3D53",
        platformDivider: "#8094A8C2",
        danger: "#F28F98",
        warn: "#E9C582",
        attention: "#FF704D",
        disabledIcon: "#536071");

    internal static readonly AppThemePalette ObsidianAmber = new(
        id: "obsidian-amber",
        name: "墨夜琥珀",
        description: "暖碳深底配琥珀金，温暖沉稳",
        isDark: true,
        isDefault: false,
        surface: "#1B1917",
        text: "#F5F0E8",
        muted: "#B8ACA0",
        field: "#131110",
        card: "#C42A2521",
        cardSolid: "#2A2521",
        stagePanel: "#A4151311",
        line: "#49413A",
        accent: "#F2B65A",
        accentHover: "#F8CA7F",
        accentPressed: "#DCA043",
        accentBorder: "#FBD89C",
        accentHoverBorder: "#FDE9C4",
        accentText: "#261805",
        buttonSurface: "#E8413932",
        buttonHover: "#F2544940",
        buttonPressed: "#E8332D27",
        buttonBorder: "#7A6C5E",
        buttonText: "#FAF5EE",
        closeButtonSurface: "#DC5E5247",
        closeButtonHover: "#EB736558",
        closeButtonPressed: "#DC4A4037",
        closeButtonBorder: "#FFF8EE",
        closeButtonText: "#FFFFFF",
        checkSurface: "#362F29",
        checkHoverSurface: "#483F36",
        checkBorder: "#9C8A78",
        checkCheckedFill: "#6B4C1E",
        checkMark: "#FFF4DF",
        inputBorder: "#6E6155",
        selection: "#7A5E36",
        sliderTrack: "#706356",
        scrollThumb: "#DFB373",
        comboArrow: "#EDE3D7",
        menuSurface: "#EE231F1C",
        menuHighlight: "#59462E",
        listHover: "#3D3630",
        navHover: "#54574C42",
        navPressed: "#70493F37",
        navSelected: "#594326",
        navSelectedBorder: "#A68149",
        dashedStroke: "#C5B6A6",
        dashedHover: "#3A63564A",
        dashedPressed: "#4D4A4037",
        pillBackground: "#1A1714",
        hintBackground: "#F02A2521",
        countBubbleBackground: "#EA24201C",
        bubbleSurface: "#463D35",
        bubbleHover: "#5A4F45",
        reorderGlyph: "#D4C7B8",
        pinIdleFill: "#66C7B6A4",
        pinIdleStroke: "#EAE0D5",
        pinPinnedSurface: "#E65E441E",
        pinPinnedHoverSurface: "#F2735425",
        pinPinnedBorder: "#F2B65A",
        pinPinnedHoverBorder: "#F9D494",
        pinPinnedFill: "#F2B65A",
        pinPinnedStroke: "#FCE7C2",
        platformTabActive: "#59B3823D",
        platformTabIdle: "#203F3832",
        platformDivider: "#80A6988A",
        danger: "#F59595",
        warn: "#F2C97D",
        attention: "#FF7A4D",
        disabledIcon: "#6B6158");

    internal static readonly AppThemePalette ForestJade = new(
        id: "forest-jade",
        name: "深林松翠",
        description: "墨森深绿配翡翠青，清润静谧",
        isDark: true,
        isDefault: false,
        surface: "#13221D",
        text: "#E9F5F0",
        muted: "#98B8AC",
        field: "#0D1915",
        card: "#C01D322B",
        cardSolid: "#1D322B",
        stagePanel: "#A00F1D18",
        line: "#304D43",
        accent: "#6EE7B7",
        accentHover: "#90F0C9",
        accentPressed: "#50D19F",
        accentBorder: "#ADF7D8",
        accentHoverBorder: "#D2FCEB",
        accentText: "#07261B",
        buttonSurface: "#E828473D",
        buttonHover: "#F2345C4F",
        buttonPressed: "#E8203A31",
        buttonBorder: "#538372",
        buttonText: "#F0FBF6",
        closeButtonSurface: "#DC3F675A",
        closeButtonHover: "#EB4F8070",
        closeButtonPressed: "#DC325449",
        closeButtonBorder: "#F0FFF9",
        closeButtonText: "#FFFFFF",
        checkSurface: "#1E3D33",
        checkHoverSurface: "#295244",
        checkBorder: "#6EA893",
        checkCheckedFill: "#1B5944",
        checkMark: "#E6FFF5",
        inputBorder: "#487666",
        selection: "#326E5B",
        sliderTrack: "#4B7869",
        scrollThumb: "#88DBBA",
        comboArrow: "#D4F0E5",
        menuSurface: "#EE172A24",
        menuHighlight: "#2A5848",
        listHover: "#243F36",
        navHover: "#5431594C",
        navPressed: "#7026483D",
        navSelected: "#215342",
        navSelectedBorder: "#4EA084",
        dashedStroke: "#9DC7B8",
        dashedHover: "#3A356353",
        dashedPressed: "#4D274A3E",
        pillBackground: "#0F1E19",
        hintBackground: "#F01D322B",
        countBubbleBackground: "#EA162923",
        bubbleSurface: "#294A3F",
        bubbleHover: "#366052",
        reorderGlyph: "#BAD6CC",
        pinIdleFill: "#6699C9B8",
        pinIdleStroke: "#D4ECE3",
        pinPinnedSurface: "#E61E5440",
        pinPinnedHoverSurface: "#F2276950",
        pinPinnedBorder: "#6EE7B7",
        pinPinnedHoverBorder: "#A5F4D4",
        pinPinnedFill: "#6EE7B7",
        pinPinnedStroke: "#D1FAE5",
        platformTabActive: "#593E9678",
        platformTabIdle: "#20244036",
        platformDivider: "#807EA899",
        danger: "#F595A0",
        warn: "#EAD084",
        attention: "#FF7652",
        disabledIcon: "#4E685F");

    internal static readonly AppThemePalette NebulaViolet = new(
        id: "nebula-violet",
        name: "星夜紫罗",
        description: "深靛夜空配薰衣草紫，优雅专注",
        isDark: true,
        isDefault: false,
        surface: "#1B192D",
        text: "#EFEFFB",
        muted: "#AAA6CA",
        field: "#131122",
        card: "#C2272440",
        cardSolid: "#272440",
        stagePanel: "#A2151326",
        line: "#423E66",
        accent: "#B99CFF",
        accentHover: "#CCB6FF",
        accentPressed: "#9F7EF2",
        accentBorder: "#DBC9FF",
        accentHoverBorder: "#ECE2FF",
        accentText: "#1A1036",
        buttonSurface: "#E839355C",
        buttonHover: "#F2494473",
        buttonPressed: "#E82D2A4A",
        buttonBorder: "#6D669E",
        buttonText: "#F5F3FF",
        closeButtonSurface: "#DC554F80",
        closeButtonHover: "#EB69629C",
        closeButtonPressed: "#DC443F69",
        closeButtonBorder: "#F8F5FF",
        closeButtonText: "#FFFFFF",
        checkSurface: "#2E2A4D",
        checkHoverSurface: "#3E3966",
        checkBorder: "#8A82BF",
        checkCheckedFill: "#4A377A",
        checkMark: "#F5F0FF",
        inputBorder: "#635D91",
        selection: "#5A498F",
        sliderTrack: "#655F94",
        scrollThumb: "#C4B0F5",
        comboArrow: "#E2DDF7",
        menuSurface: "#EE201D36",
        menuHighlight: "#4A4073",
        listHover: "#353154",
        navHover: "#54474173",
        navPressed: "#7038335C",
        navSelected: "#43366E",
        navSelectedBorder: "#816DB8",
        dashedStroke: "#B8B2DC",
        dashedHover: "#3A4E487D",
        dashedPressed: "#4D3A355E",
        pillBackground: "#161426",
        hintBackground: "#F0272440",
        countBubbleBackground: "#EA201D38",
        bubbleSurface: "#3D3861",
        bubbleHover: "#4F497A",
        reorderGlyph: "#CCC8E6",
        pinIdleFill: "#66B5AEDB",
        pinIdleStroke: "#E0DCF5",
        pinPinnedSurface: "#E6443470",
        pinPinnedHoverSurface: "#F255428A",
        pinPinnedBorder: "#B99CFF",
        pinPinnedHoverBorder: "#D7C4FF",
        pinPinnedFill: "#B99CFF",
        pinPinnedStroke: "#EDE5FF",
        platformTabActive: "#596C58B3",
        platformTabIdle: "#20322E4F",
        platformDivider: "#809690BA",
        danger: "#F595A8",
        warn: "#ECCB85",
        attention: "#FF7857",
        disabledIcon: "#5F5B7D");

    internal static readonly AppThemePalette GraphiteCoral = new(
        id: "graphite-coral",
        name: "石墨珊瑚",
        description: "暗石墨灰配柔珊瑚粉，轻盈明快",
        isDark: true,
        isDefault: false,
        surface: "#1F1B22",
        text: "#F5EEF4",
        muted: "#BCAAB8",
        field: "#161318",
        card: "#C22E2832",
        cardSolid: "#2E2832",
        stagePanel: "#A218151B",
        line: "#4D4454",
        accent: "#F48FB1",
        accentHover: "#F8AAC4",
        accentPressed: "#DF7397",
        accentBorder: "#FBC3D5",
        accentHoverBorder: "#FDE0EA",
        accentText: "#2D0E1B",
        buttonSurface: "#E8453D4B",
        buttonHover: "#F2574D5F",
        buttonPressed: "#E837303C",
        buttonBorder: "#80718A",
        buttonText: "#FBF5FA",
        closeButtonSurface: "#DC63576B",
        closeButtonHover: "#EB786A82",
        closeButtonPressed: "#DC4F4557",
        closeButtonBorder: "#FFF5FA",
        closeButtonText: "#FFFFFF",
        checkSurface: "#39313F",
        checkHoverSurface: "#4C4154",
        checkBorder: "#9E8BA8",
        checkCheckedFill: "#75354E",
        checkMark: "#FFF0F5",
        inputBorder: "#73647D",
        selection: "#7A485E",
        sliderTrack: "#756680",
        scrollThumb: "#E6A1BA",
        comboArrow: "#EFE0EB",
        menuSurface: "#EE26212A",
        menuHighlight: "#5F3B4D",
        listHover: "#3F3745",
        navHover: "#545A4E61",
        navPressed: "#70493F4F",
        navSelected: "#5E3548",
        navSelectedBorder: "#A66380",
        dashedStroke: "#CBB8C7",
        dashedHover: "#3A64576C",
        dashedPressed: "#4D4B4152",
        pillBackground: "#1B171E",
        hintBackground: "#F02E2832",
        countBubbleBackground: "#EA27222B",
        bubbleSurface: "#4A4152",
        bubbleHover: "#5E5368",
        reorderGlyph: "#D9C8D5",
        pinIdleFill: "#66C9B4C4",
        pinIdleStroke: "#EBDFEA",
        pinPinnedSurface: "#E66E324B",
        pinPinnedHoverSurface: "#F2853E5B",
        pinPinnedBorder: "#F48FB1",
        pinPinnedHoverBorder: "#F9BAD0",
        pinPinnedFill: "#F48FB1",
        pinPinnedStroke: "#FDE4ED",
        platformTabActive: "#59A85B7B",
        platformTabIdle: "#203F3845",
        platformDivider: "#80A694A3",
        danger: "#FF94A1",
        warn: "#F0C782",
        attention: "#FF6B4A",
        disabledIcon: "#6E6173");

    internal static readonly AppThemePalette DaylightMist = new(
        id: "daylight-mist",
        name: "晴昼青雾",
        description: "柔和晨雾蓝白配湖青点缀，清爽通透",
        isDark: false,
        isDefault: true,
        surface: "#EDF3FA",
        text: "#152338",
        muted: "#4A5D78",
        field: "#FFFFFF",
        card: "#E6F8FBFF",
        cardSolid: "#F8FBFF",
        stagePanel: "#DCF0F6FD",
        line: "#BDCEE2",
        accent: "#0D877A",
        accentHover: "#0A6F64",
        accentPressed: "#085D54",
        accentBorder: "#0A6B60",
        accentHoverBorder: "#07574E",
        accentText: "#FFFFFF",
        buttonSurface: "#E8D2E1F3",
        buttonHover: "#F2C0D4EC",
        buttonPressed: "#E8AFC8E6",
        buttonBorder: "#8AA4C6",
        buttonText: "#152338",
        closeButtonSurface: "#E8C9DAEE",
        closeButtonHover: "#F2B4CBE6",
        closeButtonPressed: "#E89FBCDF",
        closeButtonBorder: "#5F7DA3",
        closeButtonText: "#152338",
        checkSurface: "#FFFFFF",
        checkHoverSurface: "#E8F2FC",
        checkBorder: "#58769A",
        checkCheckedFill: "#0D877A",
        checkMark: "#FFFFFF",
        inputBorder: "#7A96B8",
        selection: "#B8DFD9",
        sliderTrack: "#A4BBD6",
        scrollThumb: "#6BA8A1",
        comboArrow: "#2A405E",
        menuSurface: "#F5F8FBFF",
        menuHighlight: "#CFE5F7",
        listHover: "#DFECF9",
        navHover: "#90C6DAEE",
        navPressed: "#B0B2CCE6",
        navSelected: "#CBECE7",
        navSelectedBorder: "#4DA89E",
        dashedStroke: "#6E8BAF",
        dashedHover: "#48BFD4EB",
        dashedPressed: "#66A9C4E3",
        pillBackground: "#EDF4FC",
        hintBackground: "#F7F9FCFF",
        countBubbleBackground: "#F4FFFFFF",
        bubbleSurface: "#D3E3F5",
        bubbleHover: "#BDD4EE",
        reorderGlyph: "#344C6B",
        pinIdleFill: "#666987AB",
        pinIdleStroke: "#283E5C",
        pinPinnedSurface: "#E6CBECE7",
        pinPinnedHoverSurface: "#F2B5E3DC",
        pinPinnedBorder: "#0D877A",
        pinPinnedHoverBorder: "#09695F",
        pinPinnedFill: "#0D877A",
        pinPinnedStroke: "#07574E",
        platformTabActive: "#669DC7E8",
        platformTabIdle: "#40D3E1F0",
        platformDivider: "#908BA3C2",
        danger: "#C82B3E",
        warn: "#A65B00",
        attention: "#D9421E",
        disabledIcon: "#8F9FB5");

    internal static readonly AppThemePalette WarmPaper = new(
        id: "warm-paper",
        name: "暖纸杏棕",
        description: "护眼暖宣纸配暖陶棕，温润舒适",
        isDark: false,
        isDefault: false,
        surface: "#F5F0E6",
        text: "#2B2118",
        muted: "#6B5948",
        field: "#FFFDF9",
        card: "#E8FCF9F3",
        cardSolid: "#FCF9F3",
        stagePanel: "#DEF5EFE4",
        line: "#D6C9B8",
        accent: "#B36222",
        accentHover: "#995119",
        accentPressed: "#804212",
        accentBorder: "#8F4D19",
        accentHoverBorder: "#753D12",
        accentText: "#FFFFFF",
        buttonSurface: "#E8E8DCCB",
        buttonHover: "#F2DCCBB4",
        buttonPressed: "#E8CEB89D",
        buttonBorder: "#A6927A",
        buttonText: "#2B2118",
        closeButtonSurface: "#E8E3D5C1",
        closeButtonHover: "#F2D5C2A8",
        closeButtonPressed: "#E8C5AE8F",
        closeButtonBorder: "#8C775E",
        closeButtonText: "#2B2118",
        checkSurface: "#FFFDF9",
        checkHoverSurface: "#F5ECE0",
        checkBorder: "#877159",
        checkCheckedFill: "#B36222",
        checkMark: "#FFFFFF",
        inputBorder: "#9E8971",
        selection: "#E8CCB0",
        sliderTrack: "#C2B19C",
        scrollThumb: "#BA8C64",
        comboArrow: "#4A3A2B",
        menuSurface: "#F5FCF9F3",
        menuHighlight: "#EEDBC4",
        listHover: "#F2E7D8",
        navHover: "#90DFCEB8",
        navPressed: "#B0D1BC9F",
        navSelected: "#F2DFC9",
        navSelectedBorder: "#C48450",
        dashedStroke: "#96816A",
        dashedHover: "#48DBC8B0",
        dashedPressed: "#66CCB497",
        pillBackground: "#F7F1E7",
        hintBackground: "#F7FCF9F3",
        countBubbleBackground: "#F4FFFDF9",
        bubbleSurface: "#E8DBC9",
        bubbleHover: "#DBC9B2",
        reorderGlyph: "#544232",
        pinIdleFill: "#6699846D",
        pinIdleStroke: "#423325",
        pinPinnedSurface: "#E6F2DFC9",
        pinPinnedHoverSurface: "#F2E8CEB0",
        pinPinnedBorder: "#B36222",
        pinPinnedHoverBorder: "#8F4D19",
        pinPinnedFill: "#B36222",
        pinPinnedStroke: "#753D12",
        platformTabActive: "#66D6AE89",
        platformTabIdle: "#40E8DDD0",
        platformDivider: "#90B5A38F",
        danger: "#BD2A38",
        warn: "#9C5506",
        attention: "#CF3D19",
        disabledIcon: "#A69788");

    internal static readonly AppThemePalette MintBreeze = new(
        id: "mint-breeze",
        name: "云杉薄荷",
        description: "清透薄荷白配森林绿，舒爽自然",
        isDark: false,
        isDefault: false,
        surface: "#ECF6F2",
        text: "#142A23",
        muted: "#45665B",
        field: "#FFFFFF",
        card: "#E8F7FCFA",
        cardSolid: "#F7FCFA",
        stagePanel: "#DCEEF7F3",
        line: "#BAD8CC",
        accent: "#167E59",
        accentHover: "#106849",
        accentPressed: "#0C543A",
        accentBorder: "#0E6345",
        accentHoverBorder: "#0A4D35",
        accentText: "#FFFFFF",
        buttonSurface: "#E8D0E8DF",
        buttonHover: "#F2BDDECF",
        buttonPressed: "#E8ABD4C1",
        buttonBorder: "#7DA897",
        buttonText: "#142A23",
        closeButtonSurface: "#E8C9E3D9",
        closeButtonHover: "#F2B4D6C8",
        closeButtonPressed: "#E89EC9B8",
        closeButtonBorder: "#5E8F7D",
        closeButtonText: "#142A23",
        checkSurface: "#FFFFFF",
        checkHoverSurface: "#E6F5EE",
        checkBorder: "#53806F",
        checkCheckedFill: "#167E59",
        checkMark: "#FFFFFF",
        inputBorder: "#74A190",
        selection: "#B8E3D2",
        sliderTrack: "#9EC2B4",
        scrollThumb: "#64AD92",
        comboArrow: "#244A3D",
        menuSurface: "#F5F7FCFA",
        menuHighlight: "#C8EAE0",
        listHover: "#DCF1EA",
        navHover: "#90C2E0D4",
        navPressed: "#B0AED4C6",
        navSelected: "#C8ECE0",
        navSelectedBorder: "#4AA686",
        dashedStroke: "#689685",
        dashedHover: "#48BADCCF",
        dashedPressed: "#66A5CFBF",
        pillBackground: "#EBF7F2",
        hintBackground: "#F7F7FCFA",
        countBubbleBackground: "#F4FFFFFF",
        bubbleSurface: "#D0E8DF",
        bubbleHover: "#BADCD0",
        reorderGlyph: "#2E5246",
        pinIdleFill: "#66669483",
        pinIdleStroke: "#214237",
        pinPinnedSurface: "#E6C8ECE0",
        pinPinnedHoverSurface: "#F2B3E3D3",
        pinPinnedBorder: "#167E59",
        pinPinnedHoverBorder: "#0E6345",
        pinPinnedFill: "#167E59",
        pinPinnedStroke: "#0A4D35",
        platformTabActive: "#668ECFB7",
        platformTabIdle: "#40D2E8E0",
        platformDivider: "#9084AD9E",
        danger: "#C42B3D",
        warn: "#9E5A00",
        attention: "#D6421C",
        disabledIcon: "#8BA89E");

    internal static readonly AppThemePalette LavenderDay = new(
        id: "lavender-day",
        name: "雾岚紫霞",
        description: "柔雾浅紫白配靛紫点缀，清雅灵动",
        isDark: false,
        isDefault: false,
        surface: "#F1EEF9",
        text: "#201B38",
        muted: "#574E78",
        field: "#FFFFFF",
        card: "#E8FAF8FF",
        cardSolid: "#FAF8FF",
        stagePanel: "#DEF0ECFA",
        line: "#CAC3E0",
        accent: "#6850C8",
        accentHover: "#563EB5",
        accentPressed: "#46309C",
        accentBorder: "#523BAE",
        accentHoverBorder: "#3F2A91",
        accentText: "#FFFFFF",
        buttonSurface: "#E8DCD5F0",
        buttonHover: "#F2CCC2E8",
        buttonPressed: "#E8BCB0E0",
        buttonBorder: "#958AB8",
        buttonText: "#201B38",
        closeButtonSurface: "#E8D6CEEB",
        closeButtonHover: "#F2C4B8E3",
        closeButtonPressed: "#E8B2A4DA",
        closeButtonBorder: "#74679E",
        closeButtonText: "#201B38",
        checkSurface: "#FFFFFF",
        checkHoverSurface: "#EEEAF8",
        checkBorder: "#6D6099",
        checkCheckedFill: "#6850C8",
        checkMark: "#FFFFFF",
        inputBorder: "#8A7EB0",
        selection: "#CFC4F0",
        sliderTrack: "#B0A6CF",
        scrollThumb: "#8E7EC7",
        comboArrow: "#352C59",
        menuSurface: "#F5FAF8FF",
        menuHighlight: "#DAD1F5",
        listHover: "#E7E2F7",
        navHover: "#90CFC6E8",
        navPressed: "#B0BEB2E0",
        navSelected: "#DCD4F5",
        navSelectedBorder: "#8773CC",
        dashedStroke: "#7E72A6",
        dashedHover: "#48C9BFE6",
        dashedPressed: "#66B7ABDC",
        pillBackground: "#F1EEFA",
        hintBackground: "#F7FAF8FF",
        countBubbleBackground: "#F4FFFFFF",
        bubbleSurface: "#DCD5F0",
        bubbleHover: "#CAC0E8",
        reorderGlyph: "#413866",
        pinIdleFill: "#667D71A3",
        pinIdleStroke: "#312854",
        pinPinnedSurface: "#E6DCD4F5",
        pinPinnedHoverSurface: "#F2CCC1F0",
        pinPinnedBorder: "#6850C8",
        pinPinnedHoverBorder: "#523BAE",
        pinPinnedFill: "#6850C8",
        pinPinnedStroke: "#3F2A91",
        platformTabActive: "#66A89AD9",
        platformTabIdle: "#40DDD7ED",
        platformDivider: "#90998FBA",
        danger: "#C72B47",
        warn: "#9E5A00",
        attention: "#D6421E",
        disabledIcon: "#9B93B3");

    internal static readonly IReadOnlyList<AppThemePalette> DarkPresets =
    [
        MidnightCyan,
        ObsidianAmber,
        ForestJade,
        NebulaViolet,
        GraphiteCoral
    ];

    internal static readonly IReadOnlyList<AppThemePalette> LightPresets =
    [
        DaylightMist,
        WarmPaper,
        MintBreeze,
        LavenderDay
    ];

    internal static readonly IReadOnlyList<AppThemePalette> AllPresets =
    [
        .. DarkPresets,
        .. LightPresets
    ];

    internal static AppThemePalette Current { get; private set; } = MidnightCyan;

    internal static event Action<AppThemePalette>? Changed;

    internal static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    internal static AppThemePalette Resolve(Preferences preferences, Func<bool>? systemIsLight = null) =>
        Resolve(preferences.ThemeMode, preferences.DarkThemePreset, preferences.LightThemePreset, systemIsLight);

    internal static AppThemePalette Resolve(ThemeMode mode, string? darkPreset, string? lightPreset, Func<bool>? systemIsLight = null)
    {
        var normalizedMode = ThemePresets.NormalizeMode(mode);
        var useLight = normalizedMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.System => (systemIsLight ?? IsSystemLightTheme)(),
            _ => false
        };
        if (useLight)
        {
            var id = ThemePresets.NormalizeLight(lightPreset);
            return LightPresets.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? DaylightMist;
        }
        else
        {
            var id = ThemePresets.NormalizeDark(darkPreset);
            return DarkPresets.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? MidnightCyan;
        }
    }

    internal static void Apply(AppThemePalette palette)
    {
        Current = palette ?? MidnightCyan;
        if (Application.Current is { Resources: { } resources })
        {
            ApplyResources(resources, Current);
        }
        Changed?.Invoke(Current);
    }

    internal static void ApplyResources(ResourceDictionary resources, AppThemePalette p)
    {
        resources["TextBrush"] = p.Text;
        resources["MutedBrush"] = p.Muted;
        resources["SurfaceBrush"] = p.Surface;
        resources["FieldBrush"] = p.Field;
        resources["CardBrush"] = p.CardSolid;
        resources["CardPanelBrush"] = p.Card;
        resources["StagePanelBrush"] = p.StagePanel;
        resources["LineBrush"] = p.Line;
        resources["AccentBrush"] = p.Accent;
        resources["ButtonBrush"] = p.ButtonSurface;
        resources["ButtonHoverBrush"] = p.ButtonHover;
        resources["ButtonPressedBrush"] = p.ButtonPressed;
        resources["ButtonBorderBrush"] = p.ButtonBorder;
        resources["ButtonTextBrush"] = p.ButtonText;
        resources["AccentHoverBrush"] = p.AccentHover;
        resources["AccentPressedBrush"] = p.AccentPressed;
        resources["AccentBorderBrush"] = p.AccentBorder;
        resources["AccentHoverBorderBrush"] = p.AccentHoverBorder;
        resources["AccentTextBrush"] = p.AccentText;
        resources["CloseButtonBrush"] = p.CloseButtonSurface;
        resources["CloseButtonHoverBrush"] = p.CloseButtonHover;
        resources["CloseButtonPressedBrush"] = p.CloseButtonPressed;
        resources["CloseButtonBorderBrush"] = p.CloseButtonBorder;
        resources["CloseButtonTextBrush"] = p.CloseButtonText;
        resources["CheckSurfaceBrush"] = p.CheckSurface;
        resources["CheckHoverSurfaceBrush"] = p.CheckHoverSurface;
        resources["CheckBorderBrush"] = p.CheckBorder;
        resources["CheckCheckedFillBrush"] = p.CheckCheckedFill;
        resources["CheckMarkBrush"] = p.CheckMark;
        resources["InputBorderBrush"] = p.InputBorder;
        resources["SelectionBrush"] = p.Selection;
        resources["SliderTrackBrush"] = p.SliderTrack;
        resources["ScrollThumbBrush"] = p.ScrollThumb;
        resources["ComboArrowBrush"] = p.ComboArrow;
        resources["MenuSurfaceBrush"] = p.MenuSurface;
        resources["MenuHighlightBrush"] = p.MenuHighlight;
        resources["ListHoverBrush"] = p.ListHover;
        resources["NavHoverBrush"] = p.NavHover;
        resources["NavPressedBrush"] = p.NavPressed;
        resources["NavSelectedBrush"] = p.NavSelected;
        resources["NavSelectedBorderBrush"] = p.NavSelectedBorder;
        resources["DashedStrokeBrush"] = p.DashedStroke;
        resources["DashedHoverBrush"] = p.DashedHover;
        resources["DashedPressedBrush"] = p.DashedPressed;
        resources["PillBackgroundBrush"] = p.PillBackground;
        resources["HintBackgroundBrush"] = p.HintBackground;
        resources["BubbleSurfaceBrush"] = p.BubbleSurface;
        resources["BubbleHoverBrush"] = p.BubbleHover;
        resources["ReorderGlyphBrush"] = p.ReorderGlyph;
        resources["DangerBrush"] = p.Danger;
        resources["WarnBrush"] = p.Warn;

        resources[SystemColors.WindowBrushKey] = p.Surface;
        resources[SystemColors.WindowTextBrushKey] = p.Text;
        resources[SystemColors.ControlBrushKey] = p.CardSolid;
        resources[SystemColors.ControlTextBrushKey] = p.Text;
        resources[SystemColors.MenuBrushKey] = p.Surface;
        resources[SystemColors.MenuTextBrushKey] = p.Text;
        resources[SystemColors.HighlightBrushKey] = p.MenuHighlight;
        resources[SystemColors.HighlightTextBrushKey] = p.Text;
    }

    internal static double RelativeLuminance(Color c)
    {
        static double Linear(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    internal static Color CompositeOver(Color fg, Color bg)
    {
        var a = fg.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(fg.R * a + bg.R * (1 - a)),
            (byte)Math.Round(fg.G * a + bg.G * (1 - a)),
            (byte)Math.Round(fg.B * a + bg.B * (1 - a)));
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
