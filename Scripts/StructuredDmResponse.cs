using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public class StructuredDmResponse
{
    [JsonPropertyName("sahnede_olanlar")]
    public string SahnedeOlanlar { get; set; } = "";

    [JsonPropertyName("aksiyon_tipi")]
    public string AksiyonTipi { get; set; } = "none";

    [JsonPropertyName("zar_beceri")]
    public string ZarBeceri { get; set; } = "";

    [JsonPropertyName("zar_dc")]
    public int ZarDc { get; set; }

    [JsonPropertyName("dusmanlar")]
    public List<string> Dusmanlar { get; set; } = new();

    [JsonPropertyName("yeni_hafiza")]
    public string YeniHafiza { get; set; } = "";

    [JsonPropertyName("anlatim")]
    public string Anlatim { get; set; } = "";

    public static object GetOllamaSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                sahnede_olanlar = new { type = "string" },
                aksiyon_tipi = new { type = "string", @enum = new[] { "none", "check", "save", "combat" } },
                zar_beceri = new { type = "string" },
                zar_dc = new { type = "integer" },
                dusmanlar = new { type = "array", items = new { type = "string" } },
                yeni_hafiza = new { type = "string" },
                anlatim = new { type = "string" }
            },
            required = new[] { "sahnede_olanlar", "aksiyon_tipi", "zar_beceri", "zar_dc", "dusmanlar", "yeni_hafiza", "anlatim" }
        };
    }

    public static bool TryParse(string rawJson, out StructuredDmResponse result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        try
        {
            result = JsonSerializer.Deserialize<StructuredDmResponse>(rawJson);
            return result != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Incrementally extracts the decoded string value of a specific top-level JSON field
/// (e.g. "anlatim") as NDJSON chunks stream in from Ollama.
/// </summary>
public class JsonFieldStreamExtractor
{
    private enum State
    {
        SeekKeyOrBrace,
        InKey,
        SeekColon,
        SeekValue,
        InTargetValue,
        InOtherString,
        Done
    }

    private readonly string _targetKey;
    private readonly StringBuilder _keyBuffer = new();
    private readonly StringBuilder _unicodeHex = new();

    private State _state = State.SeekKeyOrBrace;
    private int _depth;
    private bool _escaped;
    private int _unicodeRemaining;
    private bool _expectingColonForTarget;

    public JsonFieldStreamExtractor(string targetKey)
    {
        _targetKey = targetKey;
    }

    public string Feed(string chunk)
    {
        if (string.IsNullOrEmpty(chunk) || _state == State.Done)
            return "";

        var output = new StringBuilder();

        foreach (char ch in chunk)
        {
            switch (_state)
            {
                case State.SeekKeyOrBrace:
                    if (ch == '{')
                        _depth++;
                    else if (ch == '}')
                        _depth = Math.Max(0, _depth - 1);
                    else if (ch == '"')
                    {
                        if (_depth == 1)
                        {
                            _keyBuffer.Clear();
                            _escaped = false;
                            _state = State.InKey;
                        }
                        else
                        {
                            _escaped = false;
                            _state = State.InOtherString;
                        }
                    }
                    break;

                case State.InKey:
                    if (_escaped)
                    {
                        _keyBuffer.Append(ch);
                        _escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        _escaped = true;
                    }
                    else if (ch == '"')
                    {
                        _expectingColonForTarget = string.Equals(_keyBuffer.ToString(), _targetKey, StringComparison.Ordinal);
                        _state = _expectingColonForTarget ? State.SeekColon : State.SeekKeyOrBrace;
                    }
                    else
                    {
                        _keyBuffer.Append(ch);
                    }
                    break;

                case State.SeekColon:
                    if (ch == ':')
                        _state = State.SeekValue;
                    else if (!char.IsWhiteSpace(ch))
                        _state = State.SeekKeyOrBrace;
                    break;

                case State.SeekValue:
                    if (ch == '"')
                    {
                        _escaped = false;
                        _unicodeRemaining = 0;
                        _state = State.InTargetValue;
                    }
                    else if (!char.IsWhiteSpace(ch))
                    {
                        _state = State.SeekKeyOrBrace;
                    }
                    break;

                case State.InTargetValue:
                    if (_unicodeRemaining > 0)
                    {
                        _unicodeHex.Append(ch);
                        _unicodeRemaining--;
                        if (_unicodeRemaining == 0)
                        {
                            if (int.TryParse(_unicodeHex.ToString(), System.Globalization.NumberStyles.HexNumber, null, out int code))
                                output.Append((char)code);
                            _unicodeHex.Clear();
                        }
                        break;
                    }

                    if (_escaped)
                    {
                        _escaped = false;
                        switch (ch)
                        {
                            case '"': output.Append('"'); break;
                            case '\\': output.Append('\\'); break;
                            case '/': output.Append('/'); break;
                            case 'n': output.Append('\n'); break;
                            case 'r': break;
                            case 't': output.Append('\t'); break;
                            case 'u':
                                _unicodeHex.Clear();
                                _unicodeRemaining = 4;
                                break;
                            default: output.Append(ch); break;
                        }
                        break;
                    }

                    if (ch == '\\')
                        _escaped = true;
                    else if (ch == '"')
                        _state = State.Done;
                    else
                        output.Append(ch);
                    break;

                case State.InOtherString:
                    if (_escaped)
                        _escaped = false;
                    else if (ch == '\\')
                        _escaped = true;
                    else if (ch == '"')
                        _state = State.SeekKeyOrBrace;
                    break;
            }
        }

        return output.ToString();
    }
}
