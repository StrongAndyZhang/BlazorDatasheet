﻿using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
using BlazorDatasheet.Formula.Core;
using BlazorDatasheet.Serialization.Json.Constants;
using BlazorDatasheet.Serialization.Json.Models;

namespace BlazorDatasheet.Serialization.Json.Converters;

internal class CellJsonConverter : JsonConverter<CellModel>
{
    public override CellModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            return null;

        var cell = new CellModel();
        CellValueType valueType = CellValueType.Empty;
        JsonElement? element = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case JsonConstants.CellValueDataName:
                    element = JsonElement.ParseValue(ref reader);
                    break;
                case JsonConstants.CellValueTypeName:
                    valueType = (CellValueType)reader.GetInt32();
                    break;
                case JsonConstants.FormulaPropertyName:
                    cell.Formula = reader.GetString();
                    break;
                case JsonConstants.ColumnIndexName:
                    cell.ColIndex = reader.GetInt32();
                    break;
                case JsonConstants.MetaDataName:
                    if (JsonElement.TryParseValue(ref reader, out var el))
                    {
                        cell.MetaData = el.Value.Deserialize<Dictionary<string, object>>(options)!;
                        foreach (var kp in cell.MetaData)
                        {
                            var val = (JsonElement)kp.Value;
                            if (val.ValueKind == JsonValueKind.String)
                            {
                                cell.MetaData[kp.Key] = val.GetString();
                            }
                            else if (val.ValueKind == JsonValueKind.Number)
                            {
                                cell.MetaData[kp.Key] = val.GetDouble();
                            }
                            else if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                            {
                                cell.MetaData[kp.Key] = val.GetBoolean();
                            }
                            else
                            {
                                throw new Exception($"Unsupported meta data type for {kp.Key} type {val.ValueKind}");
                            }
                        }
                    }

                    break;
            }
        }

        cell.CellValue = CellValueHelper.GetCellValue(valueType, element);
        return cell;
    }

    public override void Write(Utf8JsonWriter writer, CellModel value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(JsonConstants.ColumnIndexName, value.ColIndex);

        if (!string.IsNullOrEmpty(value.Formula))
            writer.WriteString(JsonConstants.FormulaPropertyName, value.Formula);

        if (value.MetaData.Count > 0)
        {
            writer.WritePropertyName(JsonConstants.MetaDataName);
            JsonSerializer.Serialize(writer, value.MetaData, options);
        }

        CellValueHelper.WriteCellValue(writer, value.CellValue);
        writer.WriteEndObject();
    }
}