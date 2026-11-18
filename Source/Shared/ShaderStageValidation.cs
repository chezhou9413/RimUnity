using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RimUnity.Rendering;

//按 GLSL location 检查顶点输出和片元输入，供运行时和 ShaderTool 共用。
internal static class ShaderStageValidation
{
    private readonly record struct Varying(int Location, long EndLocation, string Name, string Type, char ScalarType, int Components);

    //一个矩阵或数组占用连续的位置，片元输入的每个位置都需要兼容的顶点输出。
    internal static void Validate(string vertex, string fragment, string vertexName, string fragmentName)
    {
        if (!TryRead(vertex, "out", out var outputs) || !TryRead(fragment, "in", out var inputs))
            return;

        foreach (var input in inputs)
        {
            long location = input.Location;
            while (location < input.EndLocation)
            {
                long coveredUntil = location;
                foreach (var output in outputs)
                {
                    if (output.Location <= location && output.EndLocation > coveredUntil &&
                        output.ScalarType == input.ScalarType && output.Components >= input.Components)
                        coveredUntil = output.EndLocation;
                }
                if (coveredUntil == location)
                    throw new InvalidDataException($"{fragmentName} 在 location {location} 读取 {input.Type} {input.Name}，"
                        + $"{vertexName} 在该位置缺少兼容的顶点输出。");
                location = coveredUntil;
            }
        }
    }

    //只检查能从声明确定位置和分量的接口。接口块、分量打包和自定义类型交由宿主管线处理。
    private static bool TryRead(string source, string direction, out List<Varying> varyings)
    {
        varyings = new List<Varying>();
        source = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", " ");
        if (Regex.IsMatch(source, @"\b(?:in|out)\s+\w+\s*\{"))
            return false;

        const string declarationPattern = @"\blayout\s*\((?<layout>[^)]*)\)\s*"
            + @"(?:(?:flat|smooth|noperspective|centroid|sample|invariant|highp|mediump|lowp)\s+)*"
            + @"(?<direction>in|out)\s+(?<type>\w+)\s+(?<name>\w+)\s*(?:\[(?<length>[^\]]*)\])?\s*;";
        const string typePattern = @"^(?:(?<scalar>float|int|uint|bool)|(?<prefix>[iub]?)vec(?<width>[234])|mat(?<columns>[234])(?:x(?<width>[234]))?)$";
        foreach (Match declaration in Regex.Matches(source, declarationPattern))
        {
            if (declaration.Groups["direction"].Value != direction)
                continue;
            string layout = declaration.Groups["layout"].Value;
            Match location = Regex.Match(layout, @"\blocation\s*=\s*([0-9]+)\b");
            if (!location.Success)
                continue;
            string type = declaration.Groups["type"].Value;
            Match shape = Regex.Match(type, typePattern);
            if (!shape.Success || Regex.IsMatch(layout, @"\bcomponent\s*="))
                return false;

            Group arrayLength = declaration.Groups["length"];
            int length = 1;
            if (arrayLength.Success && !int.TryParse(arrayLength.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out length))
                return false;
            int start = int.Parse(location.Groups[1].Value, CultureInfo.InvariantCulture);
            Group columns = shape.Groups["columns"], width = shape.Groups["width"];
            int slots = columns.Success ? columns.Value[0] - '0' : 1;
            int components = width.Success ? width.Value[0] - '0' : slots;
            string scalar = shape.Groups["scalar"].Value + shape.Groups["prefix"].Value;
            char scalarType = scalar.Length > 0 ? scalar[0] : 'f';
            varyings.Add(new Varying(start, start + (long)length * slots, declaration.Groups["name"].Value, type, scalarType, components));
        }
        return true;
    }
}
