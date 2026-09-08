using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;
using System;

public class UniWebViewGradleConfig
{
    private readonly string m_filePath;

    public UniWebViewGradleNode Root { get; }

    public UniWebViewGradleConfig(string filePath)
    {
        var file = File.ReadAllText(filePath);
        TextReader reader = new StringReader(file);

        m_filePath = filePath;
        Root = new UniWebViewGradleNode("root");
        var curNode = Root;

        var str = new StringBuilder();
        var indentationBuffer = new StringBuilder();
        var inDoubleQuote = false;
        var inSingleQuote = false;
        var inDollarVariable = false;
        var atLineStart = true;

        while (reader.Peek() > 0)
        {
            char c = (char)reader.Read();
            switch (c)
            {
                case '/':
                    {
                        if (!inDoubleQuote && !inSingleQuote && !inDollarVariable && reader.Peek() == '/')
                        {
                            var raw = str.ToString();
                            var buffered = FormatStr(str);
                            str = new StringBuilder();
                            reader.Read();
                            string comment = reader.ReadLine() ?? string.Empty;
                            if (!string.IsNullOrEmpty(buffered))
                            {
                                curNode.AppendChildNode(new UniWebViewGradleContentNode(buffered + " //" + comment, curNode));
                            }
                            else
                            {
                                var commentNode = new UniWebViewGradleCommentNode(comment, curNode)
                                {
                                    LeadingWhitespace = indentationBuffer.Length > 0 ? indentationBuffer.ToString() : ExtractLeadingWhitespace(raw)
                                };
                                curNode.AppendChildNode(commentNode);
                            }
                            indentationBuffer = new StringBuilder();
                            inDollarVariable = false;
                            atLineStart = false;
                            break;
                        }
                        str.Append('/');
                        atLineStart = false;
                        break;
                    }
                case '\n':
                case '\r':
                    {
                        var strf = FormatStr(str);
                        if (!string.IsNullOrEmpty(strf))
                        {
                            curNode.AppendChildNode(new UniWebViewGradleContentNode(strf, curNode));
                        }
                    }
                    inDollarVariable = false;
                    str = new StringBuilder();
                    indentationBuffer = new StringBuilder();
                    atLineStart = true;
                    break;
                case '\t':
                    {
                        if (atLineStart)
                        {
                            indentationBuffer.Append("    ");
                        }
                        else
                        {
                            str.Append("    ");
                            atLineStart = false;
                        }
                        break;
                    }
                case ' ':
                    {
                        if (atLineStart)
                        {
                            indentationBuffer.Append(' ');
                        }
                        else
                        {
                            str.Append(c);
                        }
                        break;
                    }
                case '{':
                    {
                        if (inDoubleQuote || inSingleQuote) {
                            str.Append(c);
                            break;
                        }
                        var raw = str.ToString();
                        var n = FormatStr(str);
                        if (!string.IsNullOrEmpty(n))
                        {
                            if (curNode != null) {
                                UniWebViewGradleNode node = new UniWebViewGradleNode(n, curNode);
                                var leading = indentationBuffer.Length > 0 ? indentationBuffer.ToString() : ExtractLeadingWhitespace(raw);
                                node.LeadingWhitespace = leading;
                                curNode.AppendChildNode(node);
                                curNode = node;
                            }
                        }
                    }
                    str = new StringBuilder();
                    indentationBuffer = new StringBuilder();
                    atLineStart = false;
                    break;
                case '}':
                    {
                        if (inDoubleQuote || inSingleQuote) {
                            str.Append(c);
                            break;
                        }
                        var strf = FormatStr(str);
                        if (!string.IsNullOrEmpty(strf))
                        {
                            curNode.AppendChildNode(new UniWebViewGradleContentNode(strf, curNode));
                        }
                        if (indentationBuffer.Length > 0)
                        {
                            curNode.ClosingWhitespace = indentationBuffer.ToString();
                        }
                        if (curNode.Parent != null) {
                            curNode = curNode.Parent;
                        }
                    }
                    str = new StringBuilder();
                    indentationBuffer = new StringBuilder();
                    atLineStart = false;
                    break;
                case '\"':
                    if (inDollarVariable) {
                        str.Append(c);
                        break;
                    }
                    inDoubleQuote = !inDoubleQuote;
                    str.Append(c);
                    atLineStart = false;
                    break;
                case '\'':
                    if (inDollarVariable) {
                        str.Append(c);
                        break;
                    }
                    inSingleQuote = !inSingleQuote;
                    str.Append(c);
                    atLineStart = false;
                    break;
                case '$': 
                    {
                        if (inDoubleQuote || inSingleQuote) {
                            str.Append(c);
                            break;
                        }
                        inDollarVariable = true;
                        str.Append(c);
                        atLineStart = false;
                        break;
                    }
                default:
                    str.Append(c);
                    if (!char.IsWhiteSpace(c))
                    {
                        atLineStart = false;
                    }
                    break;
            }
        }

        // End of file.
        var endline = FormatStr(str);
        if (!string.IsNullOrEmpty(endline))
        {
            if (curNode != null) {
                curNode.AppendChildNode(new UniWebViewGradleContentNode(endline, curNode));
            }
        }
        //Debug.Log("Gradle parse done!");
    }

    public void Save(string path = null)
    {
        if (path == null) {
            path = m_filePath;
        }
            
        File.WriteAllText(path, Print());
    }

    private static string FormatStr(StringBuilder sb)
    {
        var str = sb.ToString();
        str = str.Trim();
        return str;
    }

    private static string ExtractLeadingWhitespace(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var ch in raw)
        {
            if (ch == '\t')
            {
                sb.Append("    ");
            }
            else if (ch == ' ')
            {
                sb.Append(' ');
            }
            else if (!char.IsWhiteSpace(ch))
            {
                break;
            }
        }
        return sb.ToString();
    }
    public string Print()
    {
        StringBuilder sb = new StringBuilder();
        PrintNode(sb, Root, -1);
        // Remove the first empty line.
        sb.Remove(0, 1);
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
        {
            sb.Remove(sb.Length - 1, 1);
        }
        return sb.ToString();
    }
    private string GetLevelIndent(int level)
    {
        if (level <= 0) return "";
        return new string(' ', level * 4);
    }
    private void PrintNode(StringBuilder stringBuilder, UniWebViewGradleNode node, int level)
    {
        if (node.Parent != null) {
            var indent = node.LeadingWhitespace ?? GetLevelIndent(level);
            if (node is UniWebViewGradleCommentNode)
            {
                stringBuilder.Append("\n" + indent + @"//" + node.Name);
            }
            else
            {
                stringBuilder.Append("\n" + indent + node.Name);
            }

        }

        if (node is UniWebViewGradleContentNode || node is UniWebViewGradleCommentNode) return;
        if (node.Parent != null)  {
            stringBuilder.Append(" {");
        }
        foreach (var c in node.Children) {
            PrintNode(stringBuilder, c, level + 1);
        }
        if (node.Parent != null) {
            var indent = node.ClosingWhitespace ?? GetLevelIndent(level);
            stringBuilder.Append("\n" + indent + "}");
        }
    }
}

public class UniWebViewGradleNode
{
    protected string m_name;
    public UniWebViewGradleNode Parent { get; private set; }

    public string Name => m_name;

    public string LeadingWhitespace { get; set; }
    public string ClosingWhitespace { get; set; }

    public List<UniWebViewGradleNode> Children { get; private set; } = new List<UniWebViewGradleNode>();

    public UniWebViewGradleNode(string name, UniWebViewGradleNode parent = null)
    {
        Parent = parent;
        m_name = name;
    }

    public void Each(Action<UniWebViewGradleNode> f)
    {
        f(this);
        foreach (var n in Children)
        {
            n.Each(f);
        }
    }

    public void AppendChildNode(UniWebViewGradleNode node)
    {
        if (Children == null) Children = new List<UniWebViewGradleNode>();
        Children.Add(node);
        node.Parent = this;
    }

    public UniWebViewGradleNode TryGetNode(string path)
    {
        var subpath = path.Split('/');
        var cnode = this;
        foreach (var p in subpath)
        {
            if (string.IsNullOrEmpty(p)) continue;
            var tnode = cnode.FindChildNodeByName(p);
            if (tnode == null)
            {
                Debug.Log("Can't find Node:" + p);
                return null;
            }

            cnode = tnode;
        }

        return cnode;
    }

    public UniWebViewGradleNode FindChildNodeByName(string name)
    {
        foreach (var n in Children)
        {
            if (n is UniWebViewGradleCommentNode || n is UniWebViewGradleContentNode)
                continue;
            if (n.Name == name)
                return n;
        }
        return null;
    }

    public bool ReplaceContentStartsWith(string pattern, string value)
    {
        foreach (var n in Children)
        {
            if (!(n is UniWebViewGradleContentNode)) continue;
            if (n.m_name.StartsWith(pattern))
            {
                n.m_name = value;
                return true;
            }
        }
        return false;
    }

    public UniWebViewGradleContentNode ReplaceContentOrAddStartsWith(string pattern, string value)
    {
        foreach (var n in Children)
        {
            if (!(n is UniWebViewGradleContentNode)) continue;
            if (n.m_name.StartsWith(pattern))
            {
                n.m_name = value;
                return (UniWebViewGradleContentNode)n;
            }
        }
        return AppendContentNode(value);
    }
    
    public UniWebViewGradleContentNode AppendContentNode(string content)
    {
        foreach (var n in Children)
        {
            if (!(n is UniWebViewGradleContentNode)) continue;
            if (n.m_name == content)
            {
                Debug.Log("UniWebViewGradleContentNode with " + content + " already exists!");
                return null;
            }
        }
        UniWebViewGradleContentNode cnode = new UniWebViewGradleContentNode(content, this);
        AppendChildNode(cnode);
        return cnode;
    }


    public bool RemoveContentNode(string contentPattern)
    {
        for(int i=0;i<Children.Count;i++)
        {
            if (!(Children[i] is UniWebViewGradleContentNode)) continue;
            if(Children[i].m_name.Contains(contentPattern))
            {
                Children.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
}

public sealed class UniWebViewGradleContentNode : UniWebViewGradleNode
{
    public UniWebViewGradleContentNode(String content, UniWebViewGradleNode parent) : base(content, parent)
    {

    }

    public void SetContent(string content)
    {
        m_name = content;
    }
}

public sealed class UniWebViewGradleCommentNode : UniWebViewGradleNode
{
    public UniWebViewGradleCommentNode(String content, UniWebViewGradleNode parent) : base(content, parent)
    {

    }

    public string GetComment()
    {
        return m_name;
    }
}
