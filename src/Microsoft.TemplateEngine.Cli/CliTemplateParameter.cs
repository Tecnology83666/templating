﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Microsoft.TemplateEngine.Abstractions;

namespace Microsoft.TemplateEngine.Cli
{
    internal enum ParameterType
    {
        Boolean,
        Choice,
        Float,
        Integer,
        Hex,
        String
    }

    /// <summary>
    /// The class combines information from<see cref="ITemplateParameter"/> and <see cref="HostSpecificTemplateData"/>.
    /// Choice parameters are implemented in separate class <see cref="ChoiceTemplateParameter"/>.
    /// </summary>
    internal class CliTemplateParameter
    {
        private List<string> _shortNameOverrides = new List<string>();

        private List<string> _longNameOverrides = new List<string>();

        internal CliTemplateParameter(ITemplateParameter parameter, HostSpecificTemplateData data)
        {
            Name = parameter.Name;
            Description = parameter.Description ?? string.Empty;
            Type = ParseType(parameter.DataType);
            DefaultValue = parameter.DefaultValue;
            DataType = parameter.DataType;
            if (Type == ParameterType.Boolean && string.Equals(parameter.DefaultIfOptionWithoutValue, "true", StringComparison.OrdinalIgnoreCase))
            {
                //ignore, parser is doing this behavior by default
            }
            else
            {
                DefaultIfOptionWithoutValue = parameter.DefaultIfOptionWithoutValue;
            }
            IsRequired = parameter.Priority == TemplateParameterPriority.Required && parameter.DefaultValue == null;
            IsHidden = parameter.Priority == TemplateParameterPriority.Implicit || data.HiddenParameterNames.Contains(parameter.Name);
            AlwaysShow = data.ParametersToAlwaysShow.Contains(parameter.Name);

            if (data.ShortNameOverrides.ContainsKey(parameter.Name))
            {
                _shortNameOverrides.Add(data.ShortNameOverrides[parameter.Name]);
            }
            if (data.LongNameOverrides.ContainsKey(parameter.Name))
            {
                _longNameOverrides.Add(data.LongNameOverrides[parameter.Name]);
            }
        }

        /// <summary>
        /// Unit test constructor.
        /// </summary>
        internal CliTemplateParameter(
            string name,
            ParameterType type = ParameterType.String,
            IEnumerable<string>? shortNameOverrides = null,
            IEnumerable<string>? longNameOverrides = null)
        {
            Name = name;
            Type = type;
            _shortNameOverrides = shortNameOverrides?.ToList() ?? new List<string>();
            _longNameOverrides = longNameOverrides?.ToList() ?? new List<string>();

            Description = string.Empty;
            DefaultValue = string.Empty;
            DefaultIfOptionWithoutValue = string.Empty;
            DataType = ParameterTypeToString(Type);
        }

        internal string Name { get; private set; }

        internal string Description { get; private set; }

        internal virtual ParameterType Type { get; private set; }

        internal string DataType { get; private set; }

        internal string? DefaultValue { get; private set; }

        internal bool IsRequired { get; private set; }

        internal bool IsHidden { get; private set; }

        internal bool AlwaysShow { get; private set; }

        internal IReadOnlyList<string> ShortNameOverrides => _shortNameOverrides;

        internal IReadOnlyList<string> LongNameOverrides => _longNameOverrides;

        internal string? DefaultIfOptionWithoutValue { get; private set; }

        internal Option GetOption(IReadOnlyList<string> aliases)
        {
            Option option = GetBaseOption(aliases);
            option.IsHidden = IsHidden;
            option.IsRequired = IsRequired;
            if (!string.IsNullOrWhiteSpace(DefaultValue)
                || (Type == ParameterType.String || Type == ParameterType.Choice) && DefaultValue != null)
            {
                option.SetDefaultValue(DefaultValue);
            }
            option.Description = GetOptionDescription();
            return option;
        }

        protected virtual Option GetBaseOption(IReadOnlyList<string> aliases)
        {
            return Type switch
            {
                ParameterType.Boolean => new Option<bool>(aliases.ToArray())
                {
                    Arity = new ArgumentArity(0, 1)
                },
                ParameterType.Integer => new Option<long>(
                    aliases.ToArray(),
                    parseArgument: result => GetParseArgument(this, ConvertValueToInt)(result))
                {
                    Arity = new ArgumentArity(string.IsNullOrWhiteSpace(DefaultIfOptionWithoutValue) ? 1 : 0, 1)
                },
                ParameterType.String => new Option<string>(
                    aliases.ToArray(),
                    parseArgument: result => GetParseArgument(this, ConvertValueToString)(result))
                {
                    Arity = new ArgumentArity(DefaultIfOptionWithoutValue == null ? 1 : 0, 1)
                },
                ParameterType.Float => new Option<double>(
                    aliases.ToArray(),
                    parseArgument: result => GetParseArgument(this, ConvertValueToFloat)(result))
                {
                    Arity = new ArgumentArity(string.IsNullOrWhiteSpace(DefaultIfOptionWithoutValue) ? 1 : 0, 1)
                },
                ParameterType.Hex => new Option<long>(
                   aliases.ToArray(),
                   parseArgument: result => GetParseArgument(this, ConvertValueToHex)(result))
                {
                    Arity = new ArgumentArity(string.IsNullOrWhiteSpace(DefaultIfOptionWithoutValue) ? 1 : 0, 1)
                },
                _ => throw new Exception($"Unexpected value for {nameof(ParameterType)}: {Type}.")
            };
        }

        private static string ParameterTypeToString(ParameterType dataType)
        {
            return dataType switch
            {
                ParameterType.Boolean => "bool",
                ParameterType.Choice => "choice",
                ParameterType.Float => "float",
                ParameterType.Hex => "hex",
                ParameterType.Integer => "integer",
                _ => "text",
            };
        }

        private static ParameterType ParseType(string dataType)
        {
            return dataType switch
            {
                "bool" => ParameterType.Boolean,
                "boolean" => ParameterType.Boolean,
                "choice" => ParameterType.Choice,
                "float" => ParameterType.Float,
                "int" => ParameterType.Integer,
                "integer" => ParameterType.Integer,
                "hex" => ParameterType.Hex,
                _ => ParameterType.String
            };
        }

        private static ParseArgument<T> GetParseArgument<T>(CliTemplateParameter parameter, Func<string?, (bool, T)> convert)
        {
            return (argumentResult) =>
            {
                if (argumentResult.Parent is not OptionResult or)
                {
                    throw new NotSupportedException("The method should be only used with option.");
                }

                if (argumentResult.Tokens.Count == 0)
                {
                    if (or.IsImplicit)
                    {
                        if (parameter.DefaultValue != null)
                        {
                            (bool parsed, T value) = convert(parameter.DefaultValue);
                            if (parsed)
                            {
                                return value;
                            }
                            argumentResult.ErrorMessage = $"Cannot parse default value '{parameter.DefaultValue}' for option '{or.Token?.Value}' as expected type {typeof(T).Name}.";
                            //https://github.com/dotnet/command-line-api/blob/5eca6545a0196124cc1a66d8bd43db8945f1f1b7/src/System.CommandLine/Argument%7BT%7D.cs#L99-L113
                            //TODO: system-command-line can handle null.
                            return default!;
                        }
                        argumentResult.ErrorMessage = $"Default value for argument missing for option: {or.Token?.Value}.";
                        return default!;
                    }
                    if (parameter.DefaultIfOptionWithoutValue != null)
                    {
                        (bool parsed, T value) = convert(parameter.DefaultIfOptionWithoutValue);
                        if (parsed)
                        {
                            return value;
                        }
                        argumentResult.ErrorMessage = $"Cannot parse default if option without value '{parameter.DefaultIfOptionWithoutValue}' for option '{or.Token?.Value}' as expected type {typeof(T).Name}.";
                        return default!;
                    }
                    argumentResult.ErrorMessage = $"Required argument missing for option: {or.Token?.Value}.";
                    return default!;
                }
                else if (argumentResult.Tokens.Count == 1)
                {
                    (bool parsed, T value) = convert(argumentResult.Tokens[0].Value);
                    if (parsed)
                    {
                        return value;
                    }
                    argumentResult.ErrorMessage = $"Cannot parse argument '{argumentResult.Tokens[0].Value}' for option '{or.Token?.Value}' as expected type {typeof(T).Name}.";
                    return default!;
                }
                else
                {
                    argumentResult.ErrorMessage = $"Using more than 1 argument is not allowed for '{or.Token?.Value}', used: {argumentResult.Tokens.Count}.";
                    return default!;
                }
            };
        }

        private (bool, string) ConvertValueToString(string? value)
        {
            return (true, value ?? string.Empty);
        }

        private (bool, long) ConvertValueToInt(string? value)
        {
            if (long.TryParse(value, out long result))
            {
                return (true, result);
            }
            return (false, default);
        }

        private (bool, double) ConvertValueToFloat(string? value)
        {
            if (Utils.ParserExtensions.DoubleTryParseСurrentOrInvariant(value, out double convertedFloat))
            {
                return (true, convertedFloat);
            }
            return (false, default);
        }

        private (bool, long) ConvertValueToHex(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (false, default);
            }

            if (value.Length < 3)
            {
                return (false, default);
            }

            if (!string.Equals(value.Substring(0, 2), "0x", StringComparison.OrdinalIgnoreCase))
            {
                return (false, default);
            }

            if (long.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long convertedHex))
            {
                return (true, convertedHex);
            }
            return (false, default);
        }

        private string GetOptionDescription()
        {
            return Description;
        }
    }
}