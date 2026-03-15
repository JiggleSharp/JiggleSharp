using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace JiggleSharp.Helpers;

/// <summary>
/// Serialises and deserialises <see cref="Color"/> as a compact JSON object
/// with four numeric properties: <c>A</c>, <c>R</c>, <c>G</c>, and <c>B</c>.
///
/// <para>Example JSON representation:</para>
/// <code>{ "A": 255, "R": 255, "G": 0, "B": 0 }</code>
///
/// <para>
/// Register this converter via <see cref="JsonSerializerOptions.Converters"/>
/// when serialising any type that contains an <see cref="Color"/> property,
/// as the default <c>System.Text.Json</c> serialiser cannot handle it.
/// </para>
/// </summary>
public class AvaloniaColorJsonConverter : JsonConverter<Color>
{
    /// <summary>
    /// Reads a JSON object with <c>A</c>, <c>R</c>, <c>G</c>, <c>B</c> byte
    /// properties and constructs the corresponding <see cref="Color"/>.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the start of the color object.</param>
    /// <param name="t">The target type (always <see cref="Color"/>).</param>
    /// <param name="o">The serialiser options in effect.</param>
    /// <returns>The deserialised <see cref="Color"/>.</returns>
    public override Color Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        using var doc  = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        return Color.FromArgb(
            root.GetProperty("A").GetByte(),
            root.GetProperty("R").GetByte(),
            root.GetProperty("G").GetByte(),
            root.GetProperty("B").GetByte());
    }

    /// <summary>
    /// Writes the <see cref="Color"/> as a JSON object with four numeric
    /// properties: <c>A</c>, <c>R</c>, <c>G</c>, and <c>B</c>.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="c">The color value to serialise.</param>
    /// <param name="o">The serialiser options in effect.</param>
    public override void Write(Utf8JsonWriter writer, Color c, JsonSerializerOptions o)
    {
        writer.WriteStartObject();
        writer.WriteNumber("A", c.A);
        writer.WriteNumber("R", c.R);
        writer.WriteNumber("G", c.G);
        writer.WriteNumber("B", c.B);
        writer.WriteEndObject();
    }
}