using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace replace_tool
{
    public class LineCrawlItem
    {
        public string Original { get; set; }
        public long OriginalIndex { get; set; }
    }
    
    public class RefParameterItem
    {
        public string Original { get; set; }
        public long OriginalIndex { get; set; }
        public string Cooked { get; set; }
        public string Combined { get; set; }
        public ParameterGroup Parent { get; set; }
        public ObjectBuildItem Object { get; set; }
    }

    public class ParameterGroup
    {
        public LineCrawlItem Ref { get; set; }
        public string LeftParameter { get; set; }
        public List<RefParameterItem> LeftItems { get; set; }
        public string RightParameter { get; set; }
        public string Result { get; set; }
    }

    public class ObjectBuildItem
    {
        public RefParameterItem Parent { get; set; }
        public string Origin { get; set; }
        public List<ChildBuildItem> Children { get; set; }
    }

    public class ChildBuildItem
    {
        public RefParameterItem Parent { get; set; }
        public string Origin { get; set; }
    }
}
