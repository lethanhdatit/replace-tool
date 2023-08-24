
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using replace_tool;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

string fileName = "ClientApp\\src\\validators\\index.js";

string path = "C:\\Users\\Admin\\source\\repos\\FR\\AuvCommercial.Application\\FrontEnd\\";
string filePath = Path.Combine(path, fileName);

string metaFileName = "7.0_en_meta_v1";
string metaPath = $"C:\\Users\\Admin\\Desktop\\duplicate\\{metaFileName}.json";

string logFileName = $"keys_not_found_in_meta_pool_{metaFileName}";
string logPath = $"C:\\Users\\Admin\\Desktop\\duplicate\\{logFileName}.json";

string keyword = "window.localize.lang";

var source = File.ReadAllText(filePath);
var metaPool = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(metaPath));

source = CleanUp(source, keyword);

#region For Non-Assignment

var nonAssignmentResult = new List<ParameterGroup>();
var lines = CrawlLineDataForNonAssignment(source, keyword)
           .Where(s => !Regex.IsMatch(s.Original, @"(?==)(.[ ]*)(" + keyword + ")")
                    && !Regex.IsMatch(s.Original, "(" + keyword + ")([[])"));

foreach (var line in lines)
{
    var temp = line.Original.Split(keyword).ToList();

    if(temp.Count() > 1)
        temp.RemoveAt(0);

    foreach (var item in temp)
    {
        string rightParameter = StandardizeRightParameter(item, keyword);
        var forReplace = rightParameter;
        if (metaPool.ContainsKey(rightParameter))
        {
            rightParameter = metaPool[rightParameter].ToString();
        }
        else
        {
            string message = $"Key not found: {rightParameter}, split_item: {item}, original: {line.Original}";

            LogNotFoundKey(logPath, rightParameter, message);

            Console.WriteLine(message);

        }

        var res = line.Original.Replace(CombileString(keyword, forReplace), keyword + $"[\"{rightParameter}\"]") ;

        nonAssignmentResult.Add(new ParameterGroup
        {
            Ref = line,
            Result = res,
            RightParameter = rightParameter
        });
    }
}

foreach (var item in nonAssignmentResult)
{
    source = source.Replace(item.Ref.Original, item.Result);
}

#endregion

#region For Assignment
var parameterGroups = new List<ParameterGroup>();

var group = HandleBaseData(source, keyword);
var grLeft = group.GroupBy(g => g.LeftParameter);

foreach (var item in grLeft)
{
    ParameterGroup keep = item.FirstOrDefault();

    if (item.Count() > 1)
        keep = item.OrderByDescending(o => o.Ref.OriginalIndex).FirstOrDefault();

    if (keep != null)
    {
        keep.LeftItems = LookupRefParameter(source, keep, metaPool, logPath);
        parameterGroups.Add(keep);
    }
}

foreach (var _item in parameterGroups.GroupBy(s => s.IsBlush))
{
    if (_item.Key)
    {
        
        var orgGr = _item.ToList().GroupBy(g => g.Ref.Original);
        foreach (var gr in orgGr)
        {
            var grResult = string.Empty;

            foreach (var item in gr)
            {
                var jObj = BuildObject(item.LeftItems, keyword);
                if (jObj.HasValues)
                {
                    item.Result += $"{item.LeftParameter} = " +
                                JsonConvert.SerializeObject(jObj, Formatting.Indented)
                                           .Replace("\"", string.Empty)
                                           .Replace("\\", "\"");
                    grResult += item.Result + "\n";
                }
                else
                    item.Result = string.Empty;

            }

            source = source.Replace(gr.Key, grResult);
        }
    }
    else
    {
        foreach (var item in _item)
        {
            var jObj = BuildObject(item.LeftItems, keyword);
            if (jObj.HasValues)
            {
                item.Result += $"{item.LeftParameter} = " +
                            JsonConvert.SerializeObject(jObj, Formatting.Indented)
                                       .Replace("\"", string.Empty)
                                       .Replace("\\", "\"");

                source = Regex.Replace(source, "(" + item.LeftParameter + ")(.[ ]*)(?==)(.[ ]*)(" + CombileString(keyword, item.RightParameter) + ")", item.Result);
            }
            else
                source = source.Replace(item.Ref.Original, string.Empty);
        }
    }
}
#endregion

File.WriteAllText(filePath, source);
Console.WriteLine("All ok");


static string CleanUp(string source, string keyword)
{
    var grLeft = HandleBaseData(source, keyword).GroupBy(g => g.LeftParameter);

    foreach (var item in grLeft)
    {
        var temp = item.OrderByDescending(o => o.Ref.OriginalIndex).FirstOrDefault();

        var leftParameter = item.Key.Replace("const", string.Empty).Replace(" ", string.Empty);
        string pattern = @"(?<==|:)([ ]*)(" + leftParameter + ")";

        if (Regex.Matches(source, pattern).Count() > 0)
        {
            source = Regex.Replace(source, pattern, $" {CombileString(keyword, temp.RightParameter)}");
        }
    }

    var needNext = HandleBaseData(source, keyword).GroupBy(g => g.LeftParameter).Any(item =>
    {
        var temp = item.OrderByDescending(o => o.Ref.OriginalIndex).FirstOrDefault();

        var leftParameter = item.Key.Replace("const", string.Empty).Replace(" ", string.Empty);
        string pattern = @"(?<==|:)([ ]*)(" + leftParameter + ")";

        return Regex.Matches(source, pattern).Count() > 0;
    });

    if (needNext)
        source = CleanUp(source, keyword);

    return source;
}

static List<ParameterGroup> HandleBaseData(string source, string keyword)
{
    var group = new List<ParameterGroup>();

    foreach (var s in CrawlLineDataForAssignment(source, keyword))
    {
        var split = s.Original.Split("=");
        if (split.Length > 0 && split.Length <= 2)
        {
            var div = Regex.Match(split[0].Trim(), @"({)(.[a-zA-Z0-9_, ]*)(})");
            if (div != null && div.Success)
            {
                var paras = div.Value.Replace("{", string.Empty)
                                 .Replace("}", string.Empty)
                                 .Split(",");

                foreach (var item in paras)
                {
                    var stdLeft = StandardizeLeftParameter(item, keyword);

                    if (!string.IsNullOrWhiteSpace(stdLeft))
                        group.Add(new ParameterGroup { IsBlush = true, Ref = s, LeftParameter = $"const {stdLeft}", RightParameter = CombileString(StandardizeRightParameter(split[1], keyword), stdLeft) });
                }
            }
            else
                group.Add(new ParameterGroup { Ref = s, LeftParameter = StandardizeLeftParameter(split[0], keyword), RightParameter = StandardizeRightParameter(split[1], keyword) });
        }
    }

    return group;
}


static JObject BuildObject(List<RefParameterItem> flatItems, string keyword)
{
    var unique = flatItems.GroupBy(g => g.Cooked)
                          .Select(s => 
                          {
                              var temp = s.FirstOrDefault();
                              temp.Object = new ObjectBuildItem
                              {
                                  Origin = temp.Cooked,
                                  Children = temp.Cooked.Split(".").Select(s => new ChildBuildItem { Origin = s, Parent = temp }).ToList(),
                                  Parent = temp
                              };
                              return temp;
                          })
                          .ToList();

   
    JObject result = new JObject();

    foreach (var un in unique)
    {
        var item = un.Object.Children;

        var max = item.Count();
        int i = 0;

        result = BuildJObject(result, item, max, i, keyword);
    }

    return result;
}

static JObject BuildJObject(JObject result, List<ChildBuildItem> item, int max, int i, string keyword)
{
    var isLast = i == max - 1;

    var ch = item[i];

    if (isLast)
    {
        result[ch.Origin] = keyword + $"[\"{ch.Parent.SelectedKey}\"]";
    }
    else
    {
        i++;

        if (!result.ContainsKey(ch.Origin))
            result[ch.Origin] = new JObject();
        
        var next = BuildJObject(result[ch.Origin].ToObject<JObject>(), item, max, i, keyword);
        result[ch.Origin] = next;
    }

    return result;
}

static List<LineCrawlItem> CrawlLineDataForAssignment(string source, string keyword)
{
    return Regex.Matches(source, @"(?<=\n)(.*)(.[ ]*)(?==)(.[ ]*)(" + keyword + ")(.*)(?=\n)")
                .Select(s => new LineCrawlItem { OriginalIndex = s.Index , Original = s.Value })
                .ToList();
}

static List<LineCrawlItem> CrawlLineDataForNonAssignment(string source, string keyword)
{
    return Regex.Matches(source, @"(?<=\n)(.*)(.[ ]*)(.[ ]*)(" + keyword + ")(.*)(?=\n)")
                .Select(s => new LineCrawlItem { OriginalIndex = s.Index, Original = s.Value })
                .ToList();
}

static string StandardizeRightParameter(string source, string keyword)
{
    var temp = source.Trim();

    var ch = Regex.Matches(temp, @"[^a-zA-Z0-9_.]| ").OrderBy(o => o.Index).FirstOrDefault();
    if (ch != null)
    {
        var index = temp.IndexOf(ch.Value);
        temp = temp.Substring(0, index);
    }
    temp = temp.Replace(keyword + ".", string.Empty)
               .Replace(keyword, string.Empty)
               .Replace(" ", string.Empty);

    if (temp.StartsWith("."))
        temp = temp.Substring(1, temp.Length - 1);
    if (temp.EndsWith("."))
        temp = temp.Substring(0, temp.Length - 1);

    return temp;
}

static string StandardizeLeftParameter(string source, string keyword)
{
    var temp = source.Trim();

    var ch = Regex.Matches(temp, @"[^a-zA-Z0-9_.]| ").OrderByDescending(o => o.Index).FirstOrDefault();
    if (ch != null)
    {
        var index = temp.LastIndexOf(ch.Value);
        temp = temp.Substring(index + 1, temp.Length - index - 1);
    }

    temp = temp.Replace(keyword + ".", string.Empty)
               .Replace(keyword, string.Empty)
               .Replace(" ", string.Empty);

    if (temp.StartsWith("."))
        temp = temp.Substring(1, temp.Length - 1);
    if (temp.EndsWith("."))
        temp = temp.Substring(0, temp.Length - 1);

    return temp;
}

static List<RefParameterItem> LookupRefParameter(string source, ParameterGroup Parent, JObject metaPool, string logPath)
{
    var list = new List<RefParameterItem>();
    var leftParameter = Parent.LeftParameter.Replace("const", string.Empty).Replace(" ", string.Empty);
    var parent = Regex.Matches(source, @"(?<=" + leftParameter + "\\.)(.*)(?=\n)").ToList();

    foreach (var item in parent)
    {
        var cooked = LookupRefCore(item);
        var buildKey = CombileString(Parent.RightParameter, cooked);
        var selectedKey = buildKey;
        if (metaPool.ContainsKey(buildKey))
            selectedKey = metaPool[buildKey].ToString();
        else
        {
            string message = $"Key not found: {buildKey}, parameter: {Parent.LeftParameter}, original: {item.Value}, cooked: {cooked}";            
            LogNotFoundKey(logPath, buildKey, message);
            Console.WriteLine(message);
        }

        list.Add(new RefParameterItem
        {
            OriginalIndex = item.Index,
            Original = item.Value,
            Cooked = cooked,
            BuildKey = CombileString(Parent.RightParameter, cooked),
            Parent = Parent,
            SelectedKey = selectedKey
        });

        // loop
        if (item.Value.Contains(leftParameter))
        {
            string nextSource = item.Value;
            if (!nextSource.EndsWith("\n"))
                nextSource = nextSource + "\n";

            list.AddRange(LookupRefParameter(nextSource, Parent, metaPool, logPath));
        }
    }

    return list.Where(a => !string.IsNullOrWhiteSpace(a.Cooked)).ToList();
}

static string LookupRefCore(Match? item)
{
    string cook = string.Empty;

    var cleaned = item.Value.Replace(" ", string.Empty);
    var ch = Regex.Matches(cleaned, @"[^a-zA-Z0-9_.]").OrderBy(o => o.Index).FirstOrDefault();
    if (ch != null)
    {
        var index = item.Value.IndexOf(ch.Value);
        cook = item.Value.Substring(0, index).Replace(" ", string.Empty);
    }
    else
        cook = cleaned;

    if (cook.StartsWith("."))
        cook = cook.Substring(1, cook.Length - 1);
    if (cook.EndsWith("."))
        cook = cook.Substring(0, cook.Length - 1);

    return cook;
}

static string CombileString(string left, string right, string separator = ".")
{
    var lst = new List<string>();

    if (!string.IsNullOrWhiteSpace(left))
        lst.Add(left);

    if (!string.IsNullOrWhiteSpace(right))
        lst.Add(right);

    return string.Join(separator, lst.ToArray());
}

static void LogNotFoundKey(string logPath, string key, string message)
{
    if (!File.Exists(logPath))
    {
        File.Create(logPath).Close();
        File.WriteAllText(logPath, JsonConvert.SerializeObject(new JObject()));
    }
    var logs = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(logPath));
    if (!logs.ContainsKey(key))
        logs.Add(key, message);
    else
        logs[key] += message + "\n";

    File.WriteAllText(logPath, JsonConvert.SerializeObject(logs));
}