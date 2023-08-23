
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using replace_tool;
using System.Text;
using System.Text.RegularExpressions;

string fileName = "ClientApp\\src\\components\\trialBalance\\fileUpload\\uploadFile.js";

string path = "C:\\Users\\Admin\\source\\repos\\FR\\AuvCommercial.Application\\FrontEnd\\";
string filePath = Path.Combine(path, fileName);

string keyword = "window.localize.lang";

var source = File.ReadAllText(filePath);

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

        var res = line.Original.Replace(CombileString(keyword, rightParameter), keyword + $"[\"{rightParameter}\"]") ;

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

var group = CrawlLineDataForAssignment(source, keyword).Select(s =>
{
    var temp = s.Original.Split("=");
    return new ParameterGroup { Ref = s, LeftParameter = StandardizeLeftParameter(temp[0], keyword), RightParameter = StandardizeRightParameter(temp[1], keyword) };
})
.GroupBy(g => g.LeftParameter);

foreach (var item in group)
{
    ParameterGroup keep = item.FirstOrDefault();

    if (item.Count() > 1)
        keep = item.OrderByDescending(o => o.Ref.OriginalIndex).FirstOrDefault();

    if (keep != null)
    {
        keep.LeftItems = LookupRefParameter(source, keep);
        parameterGroups.Add(keep);
    }
}

foreach (var item in parameterGroups)
{
    var jObj = BuildObject(item.LeftItems, keyword);

    item.Result += $"{item.LeftParameter} = " +
                    JsonConvert.SerializeObject(jObj, Formatting.Indented)
                               .Replace("\"", string.Empty)
                               .Replace("\\", "\"");

    source = Regex.Replace(source, "(" + item.LeftParameter + ")(.[ ]*)(?==)(.[ ]*)(" + CombileString(keyword, item.RightParameter) + ")", item.Result);
}
#endregion

File.WriteAllText(filePath, source);
Console.WriteLine("All ok");



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
        result[ch.Origin] = keyword + $"[\"{ch.Parent.Combined}\"]";
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

static List<RefParameterItem> LookupRefParameter(string source, ParameterGroup Parent)
{
    var list = new List<RefParameterItem>();
    var parent = Regex.Matches(source, @"(?<=" + Parent.LeftParameter + "\\.)(.*)(?=\n)").ToList();

    foreach (var item in parent)
    {
        var cooked = LookupRefCore(item);

        list.Add(new RefParameterItem
        {
            OriginalIndex = item.Index,
            Original = item.Value,
            Cooked = cooked,
            Combined = CombileString(Parent.RightParameter, cooked),
            Parent = Parent
        });

        // loop
        if (item.Value.Contains(Parent.LeftParameter))
        {
            string nextSource = item.Value;
            if (!nextSource.EndsWith("\n"))
                nextSource = nextSource + "\n";

            list.AddRange(LookupRefParameter(nextSource, Parent));
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
