using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Unicode;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Localization;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one run of text inside a box, at one font pixel per unit of the entity's
/// space times the scale of the entity's <see cref="Entity.WorldTransform"/>.
/// </summary>
/// <remarks>
/// Rotation anywhere in the entity's ancestry is refused while a label is present. A negative scale
/// axis mirrors the placed run about the entity's position. Coordinates are Y-down, in world units under a world root and canvas pixels
/// under a screen root.
/// <para>
/// The box's <see cref="Pivot"/> sits at the entity's position plus <see cref="Offset"/>.
/// <see cref="HorizontalAlignment"/> and <see cref="VerticalAlignment"/> move the text inside the box
/// and never move the box. A <see cref="Size"/> of zero makes the box the rect of the
/// <see cref="ScreenEntity"/> holding it, or else the measured run.
/// </para>
/// <para>
/// A label given a <see cref="TextKey"/> shows that text in its run's active language, and shows the
/// new one from the step a <see cref="Run.Language"/> request applies. Its text and measures are
/// readable from <c>OnStart</c> on.
/// </para>
/// </remarks>
/// <param name="font">The font the run is drawn with.</param>
/// <param name="text">The text to draw. Empty by default, which draws nothing.</param>
public sealed class Label(BitmapFont font, string text = "") : Renderer
{
    /// <summary>The font the run is laid out and drawn with.</summary>
    public BitmapFont Font
    {
        get;

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = font ?? throw new ArgumentNullException(nameof(font));

    // The text in the form it was last written: either a string, or a span copied into a buffer this label
    // owns and grows only when a longer span arrives. _text holds the string form and is null after a span
    // write until Text builds it. Layout reads _memory, and neither path allocates in Draw.
    private string? _text = text ?? throw new ArgumentNullException(nameof(text));
    private ReadOnlyMemory<char> _memory = text.AsMemory();
    private char[] _buffer = [];

    // Counts every write to the text. The cached layout reads it to tell the characters changed.
    private int _textVersion;

    // The key the text resolves from while _keyed, and the text of each argument formatted once at the
    // call, each ending at its entry in _argumentEnds. A language change substitutes them again.
    private TextKey _key;
    private bool _keyed;
    private char[] _arguments = [];
    private ArgumentEnds _argumentEnds;
    private ArgumentCounts _counts;
    private int _negatives;
    private int _argumentCount;

    // The run and its language version the keyed text was resolved under. A null run resolves on the next read.
    private Run? _resolvedRun;
    private int _resolvedVersion;

    // The run laid out, built on the first draw or bounds read and kept until something it depends on changes.
    private LabelLayout? _layout;

    /// <summary>The text to draw. Empty draws nothing.</summary>
    /// <remarks>
    /// A <c>\n</c> starts a new line, and a codepoint <see cref="Font"/> has no glyph for draws
    /// nothing and advances nothing. After <see cref="SetText(ReadOnlySpan{char})"/> the getter returns the same
    /// characters as a string, built on the first read after each write. A keyed label reads back its
    /// text in the run's active language, and writing a string replaces the key.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The label is keyed, and its entity is in no running scene yet.</exception>
    public string Text
    {
        get
        {
            Resolve();

            return _text ??= _memory.ToString();
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _keyed = false;
            _text = value;
            _memory = value.AsMemory();
            _textVersion++;
        }
    }

    /// <summary>
    /// Sets <see cref="Text"/> from a span, for a readout rewritten every frame.
    /// </summary>
    /// <remarks>
    /// The label copies the characters into a buffer it owns. The caller's span may change as soon as
    /// this returns. The call allocates nothing once the label has held a run this long.
    /// </remarks>
    public void SetText(ReadOnlySpan<char> text)
    {
        if (_buffer.Length < text.Length)
        {
            _buffer = new char[Math.Max(text.Length, _buffer.Length * 2)];
        }

        _keyed = false;
        text.CopyTo(_buffer);
        _memory = _buffer.AsMemory(0, text.Length);
        _text = null;
        _textVersion++;
    }

    /// <summary>Shows <paramref name="text"/> in the run's active language, in place of any text or key before it.</summary>
    /// <param name="text">A <c>CapsuleStrings</c> member taking no arguments.</param>
    public void SetText(TextKey text) => Key(text, 0, 0);

    /// <summary>Shows <paramref name="text"/> in the run's active language with its one placeholder filled.</summary>
    /// <remarks>
    /// The argument is formatted once, now, with the invariant culture, and kept across a language change.
    /// Optimized code formats a value type without boxing. A placeholder that selects a plural form takes
    /// an integer type, <see langword="sbyte"/> to <see langword="ulong"/>, and a language change selects
    /// the form again.
    /// </remarks>
    /// <example>
    /// <code>
    /// label.SetText(CapsuleStrings.Options.WindowScale, scale);
    /// </code>
    /// </example>
    /// <param name="text">A <c>CapsuleStrings</c> member taking one argument.</param>
    /// <param name="arg0">The value of the text's first placeholder.</param>
    public void SetText<T0>(TextKey text, T0 arg0)
    {
        Key(text, 1, Integer<T0>(0));
        Append(arg0);
    }

    /// <summary>Shows <paramref name="text"/> in the run's active language with its two placeholders filled.</summary>
    /// <remarks>Each argument is formatted once, as <see cref="SetText{T0}(TextKey, T0)"/> formats one.</remarks>
    /// <param name="text">A <c>CapsuleStrings</c> member taking two arguments.</param>
    /// <param name="arg0">The value of the text's first placeholder.</param>
    /// <param name="arg1">The value of the text's second placeholder.</param>
    public void SetText<T0, T1>(TextKey text, T0 arg0, T1 arg1)
    {
        Key(text, 2, Integer<T0>(0) | Integer<T1>(1));
        Append(arg0);
        Append(arg1);
    }

    /// <summary>Shows <paramref name="text"/> in the run's active language with its three placeholders filled.</summary>
    /// <remarks>Each argument is formatted once, as <see cref="SetText{T0}(TextKey, T0)"/> formats one.</remarks>
    /// <param name="text">A <c>CapsuleStrings</c> member taking three arguments.</param>
    /// <param name="arg0">The value of the text's first placeholder.</param>
    /// <param name="arg1">The value of the text's second placeholder.</param>
    /// <param name="arg2">The value of the text's third placeholder.</param>
    public void SetText<T0, T1, T2>(TextKey text, T0 arg0, T1 arg1, T2 arg2)
    {
        Key(text, 3, Integer<T0>(0) | Integer<T1>(1) | Integer<T2>(2));
        Append(arg0);
        Append(arg1);
        Append(arg2);
    }

    /// <summary>
    /// Added to the entity's position to give the point the box's <see cref="Pivot"/> sits on, in the
    /// entity's own units. Zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// The point on the box that sits at the entity's position plus <see cref="Offset"/>. Defaults to
    /// <see cref="Capsule.Rendering.Pivot.TopLeft"/>.
    /// </summary>
    public Pivot Pivot { get; set; }

    /// <summary>
    /// The box the run is laid out in, in the entity's units. A non-positive component uses the
    /// measured run on that axis, which is the default on both axes.
    /// </summary>
    /// <remarks>
    /// A zero axis on a <see cref="ScreenEntity"/> takes the entity's rect extent first, and the measured
    /// run where that extent is zero too. On an axis the rect fills, <see cref="Pivot"/> reads zero and the
    /// box is the rect itself. <see cref="Wrap"/> wraps inside a positive X.
    /// </remarks>
    public Vector2 Size { get; set; }

    /// <summary>
    /// Whether a line wider than <see cref="Size"/> breaks inside the box. Defaults to
    /// <see cref="TextWrap.None"/>.
    /// </summary>
    public TextWrap Wrap { get; set; }

    /// <summary>
    /// Where each line sits between the box's left and right edges. Defaults to
    /// <see cref="Capsule.Rendering.HorizontalAlignment.Left"/>.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment { get; set; }

    /// <summary>
    /// Where the run sits between the box's top and bottom edges. Defaults to
    /// <see cref="Capsule.Rendering.VerticalAlignment.Top"/>.
    /// </summary>
    public VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>
    /// How many of <see cref="Text"/>'s leading codepoints are drawn, or null, the default, to draw
    /// them all. Zero draws nothing.
    /// </summary>
    /// <remarks>
    /// Layout always runs over the whole text. Revealing a line one codepoint at a time never
    /// reflows it. A line break and a codepoint the font has no glyph for each count as one
    /// codepoint.
    /// </remarks>
    public int? VisibleCharacters { get; set; }

    /// <summary>The first laid-out line drawn, at the box's top. Zero by default, which draws from the first line.</summary>
    /// <remarks>
    /// With <see cref="MaxLines"/> it windows the run, and measures and alignment cover only the window.
    /// <see cref="VisibleCharacters"/> still counts from the start of <see cref="Text"/>. A page reveals
    /// from <see cref="LineStart"/> of its first line.
    /// </remarks>
    /// <example>
    /// Typing one page, then turning to the next:
    /// <code>
    /// int end = label.LineStart(label.FirstLine + label.MaxLines!.Value);
    /// for (int shown = label.LineStart(label.FirstLine) + 1; shown &lt;= end; shown++)
    /// {
    ///     label.VisibleCharacters = shown;
    ///     yield return Wait.Ticks(2);
    /// }
    ///
    /// label.FirstLine += label.MaxLines.Value;
    /// </code>
    /// </example>
    public int FirstLine
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>How many lines from <see cref="FirstLine"/> draw, or null, the default, to draw every line from it.</summary>
    public int? MaxLines
    {
        get;
        set
        {
            if (value is { } max)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Font pixels added between one line and the next, before the entity's scale. Zero by default, which
    /// sets lines <see cref="BitmapFont.LineHeight"/> apart, and negative is refused.
    /// </summary>
    /// <remarks><see cref="ContentSize"/> and an unsized box include it, between lines and never below the last.</remarks>
    public int LineSpacing
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// The shapes the lines flow around, in the box's own units from its top-left corner. Empty by default,
    /// which lets every line span the box.
    /// </summary>
    /// <remarks>
    /// Rows run down from the box's top, each <see cref="BitmapFont.LineHeight"/> tall and
    /// <see cref="LineSpacing"/> below the last. Each shape blocks its exact extent across a row it overlaps.
    /// A line wraps in the widest span the shapes leave its row, the left on a tie, and aligns within it.
    /// A line takes one span and never both sides of a shape. A row with no span left holds an empty line.
    /// With <see cref="MaxLines"/> the shapes stand on every page, line L in row L modulo
    /// <see cref="MaxLines"/>. Turning a page never reflows. They shape only a word-wrapped, top-aligned
    /// label with a box of positive width, and shapes that leave no room on any row of a page shape
    /// nothing. <see cref="LineCount"/>, <see cref="LineStart"/>, <see cref="ContentSize"/> and
    /// <see cref="Bounds"/> read the narrowed lines. Assign a new array to change them.
    /// </remarks>
    /// <example>
    /// A portrait in the box's top-left corner:
    /// <code>
    /// label.Exclusions = [Shape2D.Box(Vector2.Zero, new Vector2(32f, 32f))];
    /// </code>
    /// </example>
    public ImmutableArray<Shape2D> Exclusions
    {
        get;
        set
        {
            if (value.IsDefault)
            {
                throw new ArgumentException("Exclusions is a default ImmutableArray. Assign [] for no exclusions.", nameof(value));
            }

            foreach (Shape2D shape in value)
            {
                Shape2D.RequireBuilt(shape, nameof(value));
            }

            field = value;
        }
    } = [];

    /// <summary>
    /// The lines the whole of <see cref="Text"/> lays out to in the box, wraps and <c>\n</c> breaks
    /// included, whatever <see cref="FirstLine"/> and <see cref="MaxLines"/> say.
    /// </summary>
    /// <remarks>
    /// It counts up to the last line that draws a glyph. A trailing blank line is not counted. Reading it
    /// lays the run out under the entity's current scale and box, and at unit scale on no entity.
    /// </remarks>
    public int LineCount => Laid(LayoutScale, out _, out _).LineCount(_memory.Span);

    /// <summary>
    /// Where <paramref name="line"/> starts, as a count of <see cref="VisibleCharacters"/> that draws every
    /// line before it and none of its own.
    /// </summary>
    /// <remarks>
    /// A line a wrap broke at a space starts after the space, and one after a <c>\n</c> after the break. A
    /// line at or past <see cref="LineCount"/> returns the codepoint count of the whole text, which draws
    /// everything. With <see cref="RichText"/> no tag is counted.
    /// </remarks>
    /// <param name="line">A laid-out line, counted from the first line of the text.</param>
    public int LineStart(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);

        return Laid(LayoutScale, out _, out _).LineStart(_memory.Span, line);
    }

    /// <summary>Whether <see cref="Text"/> is read as markup, defaulting to false to draw it exactly as written.</summary>
    /// <remarks>
    /// <c>[color=#rrggbb]...[/color]</c> tints the glyphs between the tags, and <c>#rrggbbaa</c> adds an
    /// alpha. Spans nest, the innermost wins, and an unclosed span runs to the end. A glyph's colour is its
    /// span's multiplied by <see cref="Color"/>. <c>[[</c> draws one <c>[</c>. Any other bracketed text
    /// draws as written, including an unknown tag, a malformed colour and a stray <c>[/color]</c>.
    /// <see cref="VisibleCharacters"/> counts no tag.
    /// </remarks>
    /// <example>
    /// <code>
    /// new Label(font, "Press [color=#ffd060]Start[/color]") { RichText = true };
    /// </code>
    /// </example>
    public bool RichText { get; set; }

    /// <summary>A tint multiplied into every texel. White by default, which draws the font pages unchanged.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>Starts a label showing <paramref name="text"/> in its run's active language.</summary>
    /// <param name="font">The font the run is drawn with.</param>
    /// <param name="text">The text to show, a <c>CapsuleStrings</c> member taking no arguments.</param>
    public Label(BitmapFont font, TextKey text)
        : this(font)
    {
        SetText(text);
    }

    internal override bool Steps => false;

    internal override TransformSupport Supports => TransformSupport.Scale;

    /// <summary>
    /// The box this label lays its text out in. It uses <see cref="Size"/> on each axis where that
    /// is positive and the measured run elsewhere, placed by <see cref="Pivot"/> at the entity's
    /// position plus <see cref="Offset"/>.
    /// </summary>
    /// <remarks>
    /// The layout is kept until the text, the font, the box width, <see cref="Wrap"/>,
    /// <see cref="HorizontalAlignment"/>, <see cref="RichText"/>, the line window,
    /// <see cref="LineSpacing"/> or <see cref="Exclusions"/> changes, in the space and under the rules <see cref="Renderer.Bounds"/>
    /// states.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The label is keyed, and its entity is in no running scene yet.</exception>
    public override Rect Bounds
    {
        get
        {
            Resolve();
            if (Entity is null)
            {
                return default;
            }

            Transform2D current = RenderTransform;
            LabelLayout layout = Placed(current.Scale, current.TransformPoint(Offset), out Rect placed, out _, out _);

            return layout.IsEmpty ? default : placed;
        }
    }

    /// <summary>
    /// The laid-out text's extent in the entity's units, its widest line by its lines' total height, or zero
    /// when it draws nothing.
    /// </summary>
    /// <remarks>
    /// It ignores the box and counts only the lines <see cref="FirstLine"/> and <see cref="MaxLines"/>
    /// draw. A parent can fit itself to what a label filling it draws, or place something where the text
    /// ends.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The label is keyed, and its entity is in no running scene yet.</exception>
    public Vector2 ContentSize
    {
        get
        {
            Resolve();
            if (Entity is null)
            {
                return default;
            }

            LabelLayout layout = Laid(Vector2.Abs(RenderTransform.Scale), out _, out _);

            return layout.IsEmpty ? default : new Vector2(layout.Width, layout.Height);
        }
    }

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        assets.Add(Font.Pages);
    }

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Transform2D current = RenderTransform;
        Vector2 scale = current.Scale;
        Vector2 position = current.TransformPoint(Offset);
        Vector2 previous = PreviousRenderTransform.TransformPoint(Offset);
        LabelLayout layout = Placed(scale, position, out Rect placed, out Vector2 origin, out Vector2 size);
        if (layout.IsEmpty)
        {
            return;
        }

        if (view.Rejects(TextBox.Reach(previous, position, size, Vector2.Abs(scale), Font.Overhang, placed), view.Space))
        {
            return;
        }

        ReadOnlySpan<TextureHandle> pages = Font.Pages;

        // The run is laid out in full whatever the visible count. Revealing it never moves a glyph.
        int visible = VisibleCharacters ?? int.MaxValue;

        // The box is the same shape at both ends of the step. The previous origin differs only by travel.
        Vector2 travel = previous - position;

        foreach (LabelGlyph glyph in layout.Glyphs)
        {
            if (glyph.Index >= visible)
            {
                return;
            }

            Sprite sprite = new(pages[glyph.Page], glyph.Region);
            Vector2 at = origin + (new Vector2(glyph.X, glyph.Y) * scale);
            // A negative scale axis folds into the flip, and the renderer draws the magnitude of the extent.
            Vector2 extent = new Vector2(sprite.Region.Width, sprite.Region.Height) * scale;

            view.Add(
                new SpriteIntent(
                    sprite,
                    at + travel,
                    at,
                    PreviousRotation: 0f,
                    Rotation: 0f,
                    Vector2.Abs(extent),
                    FlipX: extent.X < 0f,
                    FlipY: extent.Y < 0f,
                    glyph.Tint == ColorRgba.White ? Color : ColorRgba.Multiply(glyph.Tint, Color)));
        }
    }

    // Brings the layout up to date for the scale it will draw at. box is what the entity gives the label in
    // its own units, and size the same in the drawn space's.
    private LabelLayout Laid(Vector2 scale, out Vector2 box, out Vector2 size)
    {
        Resolve();
        box = ScreenEntity.Fill(Entity, Size);
        size = box * scale;

        LabelLayoutKey key = new(
            _textVersion,
            Font,
            size.X > 0f ? TextBox.WrapWidth(size.X, scale.X) : -1,
            Wrap,
            HorizontalAlignment,
            RichText,
            FirstLine,
            MaxLines,
            LineSpacing,
            !Exclusions.IsEmpty && Wrap == TextWrap.Word && VerticalAlignment == VerticalAlignment.Top && size.X > 0f ? Exclusions : default);

        _layout ??= new LabelLayout();
        _layout.Update(_memory.Span, in key);

        return _layout;
    }

    // 1 << index when T is an integer type a plural form selects on, else 0. The JIT folds it to a constant.
    private static int Integer<T>(int index) =>
        typeof(T) == typeof(sbyte) || typeof(T) == typeof(byte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort)
        || typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong)
            ? 1 << index
            : 0;

    // An integer value's magnitude, and whether it is negative. The JIT keeps only T's branch.
    private static ulong Magnitude<T>(T value, out bool negative)
    {
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(ushort) || typeof(T) == typeof(uint) || typeof(T) == typeof(ulong))
        {
            negative = false;

            return typeof(T) == typeof(byte) ? Unsafe.As<T, byte>(ref value)
                : typeof(T) == typeof(ushort) ? Unsafe.As<T, ushort>(ref value)
                : typeof(T) == typeof(uint) ? Unsafe.As<T, uint>(ref value)
                : Unsafe.As<T, ulong>(ref value);
        }

        long signed = typeof(T) == typeof(sbyte) ? Unsafe.As<T, sbyte>(ref value)
            : typeof(T) == typeof(short) ? Unsafe.As<T, short>(ref value)
            : typeof(T) == typeof(int) ? Unsafe.As<T, int>(ref value)
            : Unsafe.As<T, long>(ref value);
        negative = signed < 0;

        // Negating long.MinValue wraps to itself, whose bits as a ulong are its magnitude.
        return negative ? unchecked((ulong)-signed) : (ulong)signed;
    }

    // Takes a key, once its argument count is checked and every argument that selects a plural form is an
    // integer, and drops the arguments of any key before it.
    private void Key(TextKey text, int arguments, int integers)
    {
        _ = text.Required(nameof(text));
        if (text.ArgumentCount != arguments)
        {
            throw new ArgumentException(
                $"The text '{text}' takes {text.ArgumentCount} argument(s), and {arguments} were passed. Call the SetText overload taking {text.ArgumentCount}.",
                nameof(text));
        }

        if ((text.PluralMask & ~integers) is not 0 and int refused)
        {
            throw new ArgumentException(
                $"The text '{text}' selects a plural form on argument {int.TrailingZeroCount(refused)}, which was passed no integer. Pass a count of an integer type, sbyte to ulong.",
                nameof(text));
        }

        _key = text;
        _keyed = true;
        _argumentCount = 0;
        _resolvedRun = null;
    }

    // Formats one argument after the ones before it, growing the buffer they share while it is too short.
    // An integer is also kept as the count a plural form selects on.
    private void Append<T>(T value)
    {
        int start = _argumentCount == 0 ? 0 : _argumentEnds[_argumentCount - 1];
        int written;
        if (Integer<T>(0) != 0)
        {
            _counts[_argumentCount] = Magnitude(value, out bool negative);
            _negatives = negative ? _negatives | (1 << _argumentCount) : _negatives & ~(1 << _argumentCount);
        }

        if (value is string text)
        {
            Reserve(ref _arguments, start + text.Length);
            text.CopyTo(_arguments.AsSpan(start));
            written = text.Length;
        }
        else if (value is ISpanFormattable)
        {
            // Optimized code (tiered JIT, Release, NativeAOT) elides the box this cast makes of a value
            // type, as the runtime's own interpolated-string handler relies on. Debug code boxes.
            while (!((ISpanFormattable)value).TryFormat(_arguments.AsSpan(start), out written, default, CultureInfo.InvariantCulture))
            {
                Reserve(ref _arguments, Math.Max(start + 16, _arguments.Length * 2));
            }
        }
        else
        {
            string formatted = value?.ToString() ?? string.Empty;
            Reserve(ref _arguments, start + formatted.Length);
            formatted.CopyTo(_arguments.AsSpan(start));
            written = formatted.Length;
        }

        _argumentEnds[_argumentCount++] = start + written;
    }

    // Grows buffer to at least length, keeping what it holds.
    private static void Reserve(ref char[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            Array.Resize(ref buffer, Math.Max(length, buffer.Length * 2));
        }
    }

    // Brings a keyed label's text up to its run's active language: one compare while that is unchanged.
    // The template is decoded into the label's own buffer with each argument's text in its placeholder.
    private void Resolve()
    {
        if (!_keyed)
        {
            return;
        }

        Run run = Entity?.SceneOrNull?.RunOrNull ?? throw new InvalidOperationException(
            $"This label shows the text '{_key}', which resolves against its run's language. Read its text or measures from OnStart on, once its entity is in a running scene.");
        if (run == _resolvedRun && run.LanguageVersion == _resolvedVersion)
        {
            return;
        }

        _resolvedRun = run;
        _resolvedVersion = run.LanguageVersion;
        StringTable table = _key.Table;
        int language = table.Resolve(run.ActiveLanguage);
        ReadOnlySpan<byte> template = table.Template(language, _key.Index);

        // A slot the language lacks holds the source text, whose plurals take the source language's rules.
        if (!template.IsEmpty && template[0] == StringTable.SourceText)
        {
            template = template[1..];
            language = 0;
        }

        int length = Write(table, language, template, counted: -1, length: 0);
        _memory = _buffer.AsMemory(0, length);
        _text = null;
        _textVersion++;
    }

    // Decodes template into the buffer from length on, and returns the new length. A plural block writes
    // the branch its count selects, in which each count mark writes the text of argument counted.
    private int Write(StringTable table, int language, ReadOnlySpan<byte> template, int counted, int length)
    {
        while (true)
        {
            int marker = template.IndexOfAny(StringTable.Placeholder, StringTable.PluralOpen, StringTable.Count);
            ReadOnlySpan<byte> literal = marker < 0 ? template : template[..marker];

            // A UTF-8 byte decodes to at most one char.
            Reserve(ref _buffer, length + literal.Length);
            _ = Utf8.ToUtf16(literal, _buffer.AsSpan(length), out _, out int decoded);
            length += decoded;
            if (marker < 0)
            {
                return length;
            }

            byte code = template[marker];
            if (code == StringTable.Count)
            {
                length = WriteArgument(counted, length);
                template = template[(marker + 1)..];
                continue;
            }

            int argument = template[marker + 1] - '0';
            template = template[(marker + 2)..];
            if (code == StringTable.Placeholder)
            {
                length = WriteArgument(argument, length);
                continue;
            }

            int close = template.IndexOf(StringTable.PluralClose);
            ulong magnitude = _counts[argument];
            ReadOnlySpan<byte> branch = StringTable.Branch(
                template[..close], magnitude, (_negatives & (1 << argument)) != 0, table.Category(language, magnitude));
            length = Write(table, language, branch, argument, length);
            template = template[(close + 1)..];
        }
    }

    // Copies argument's formatted text into the buffer at length, and returns the new length.
    private int WriteArgument(int argument, int length)
    {
        int start = argument == 0 ? 0 : _argumentEnds[argument - 1];
        int count = _argumentEnds[argument] - start;
        Reserve(ref _buffer, length + count);
        _arguments.AsSpan(start, count).CopyTo(_buffer.AsSpan(length));

        return length + count;
    }

    // The scale a line query lays the run out at, which is the one the next draw uses.
    private Vector2 LayoutScale => Entity is null ? Vector2.One : Vector2.Abs(RenderTransform.Scale);

    // Lays the run out and places it as an unmirrored scale would, then mirrors the placed box and the pen start
    // about position on each axis the scale turns negative. size is the box in the drawn space's units.
    private LabelLayout Placed(Vector2 scale, Vector2 position, out Rect placed, out Vector2 origin, out Vector2 size)
    {
        Vector2 magnitude = Vector2.Abs(scale);
        LabelLayout layout = Laid(magnitude, out Vector2 box, out size);
        Rect upright = Place(layout, position, magnitude, box, size, out Vector2 pen);

        Vector2 sign = new(scale.X < 0f ? -1f : 1f, scale.Y < 0f ? -1f : 1f);
        Vector2 near = position + ((upright.Position - position) * sign);
        Vector2 far = position + ((new Vector2(upright.Right, upright.Bottom) - position) * sign);
        placed = new Rect(Vector2.Min(near, far), Vector2.Abs(far - near));
        origin = position + ((pen - position) * sign);

        return layout;
    }

    private Rect Place(LabelLayout layout, Vector2 position, Vector2 scale, Vector2 box, Vector2 size, out Vector2 origin)
    {
        Vector2 measured = new Vector2(layout.Width, layout.Height) * scale;

        // An axis the fill changed is the screen entity's rect, which starts at the entity's position.
        Pivot pivot = new(box.X != Size.X ? 0f : Pivot.X, box.Y != Size.Y ? 0f : Pivot.Y);

        return TextBox.Place(position, pivot, TextBox.Extent(size, measured), measured, VerticalAlignment, out origin);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Text", Text);
        panel.Field("RichText", RichText);
        panel.Field("Font", string.Create(CultureInfo.InvariantCulture, $"{Font.LineHeight}px line"));
        panel.Field("Pivot", new Vector2(Pivot.X, Pivot.Y));
        panel.Field("Offset", Offset);
        panel.Field("Size", Size);
        panel.Field("Color", Color);
        panel.Field("Wrap", Wrap);
        panel.Field("HorizontalAlignment", HorizontalAlignment);
        panel.Field("VerticalAlignment", VerticalAlignment);
        panel.Field("VisibleCharacters", VisibleCharacters?.ToString(CultureInfo.InvariantCulture));
        panel.Field("FirstLine", FirstLine);
        panel.Field("MaxLines", MaxLines?.ToString(CultureInfo.InvariantCulture));
        panel.Field("LineSpacing", LineSpacing);
        panel.Field("Exclusions", Exclusions.Length);
    }

    // The end of each argument's text in _arguments, one per placeholder a key can take.
    [InlineArray(3)]
    private struct ArgumentEnds
    {
        private int _end;
    }

    // The magnitude of each integer argument, the count a plural form selects on. _negatives holds the signs.
    [InlineArray(3)]
    private struct ArgumentCounts
    {
        private ulong _count;
    }
}
