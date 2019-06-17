﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Materia.Nodes.MathNodes;
using Newtonsoft.Json;
using Materia.Imaging;
using Materia.Shaders;
using Materia.MathHelpers;
using Materia.Nodes.Attributes;
using System.Reflection;
using OpenTK;
using NLog;

namespace Materia.Nodes
{
    public class FunctionGraph : Graph
    {
        private static ILogger Log = LogManager.GetCurrentClassLogger();

        static string GLSLHash = "float rand(vec2 co) {\r\n"
                                 + "return fract(sin(dot(co, vec2(12.9898,78.233))) * 43758.5453) * 2.0 - 1.0;\r\n"
                                 + "}\r\n\r\n";

        public Node OutputNode { get; protected set; }

        public GLShaderProgram Shader { get; protected set; }

        [HideProperty]
        public NodeType ExpectedOutput
        {
            get; set;
        }

        [HideProperty]
        public bool HasExpectedOutput
        {
            get
            {
                if (OutputNode == null) return false;
                if (OutputNode.Outputs == null) return false;
                if (OutputNode.Outputs.Count == 0) return false;

                var type = GetOutputType();

                if (type == null) return false;

                return (ExpectedOutput & type) != 0;
            }
        }

        public object Result
        {
            get; set;
        }

        protected Graph parentGraph;
        public Graph ParentGraph
        {
            get
            {
                return parentGraph;
            }
            set
            {
                if(parentGraph != null)
                {
                    parentGraph.OnGraphUpdated -= G_OnGraphUpdated;
                }

                parentGraph = value;
                
                if(parentGraph != null)
                {
                    parentGraph.OnGraphUpdated += G_OnGraphUpdated;
                }

                int c = Nodes.Count;
                for(int i = 0; i < c; i++)
                {
                    MathNode n = (MathNode)Nodes[i];
                    n.OnFunctionParentSet();
                }
            }
        }

        public override Node ParentNode
        {
            get
            {
                return parentNode;
            }
            set
            {
                Graph g = TopGraph();

                if(g != null)
                {
                    g.OnGraphUpdated -= G_OnGraphUpdated;
                }

                parentNode = value;
                //update nodes
                int c = Nodes.Count;
                for(int i = 0; i < c; i++)
                {
                    MathNode n = (MathNode)Nodes[i];
                    n.ParentNode = parentNode;
                }

                g = TopGraph();
                if(g != null)
                {
                    g.OnGraphUpdated += G_OnGraphUpdated;
                }
            }
        }

        private void G_OnGraphUpdated(Graph g)
        {
            randomSeed = g.RandomSeed;
            SetVar("RandomSeed", randomSeed);
        }

        [HideProperty]
        public new int Width
        {
            get
            {
                return width;
            }
            set
            {
                width = value;
            }
        }

        [HideProperty]
        public new int Height
        {
            get
            {
                return height;
            }
            set
            {
                height = value;
            }
        }

        public FunctionGraph(string name, int w = 256, int h = 256) : base(name, w, h)
        {
            Name = name;
            SetVar("PI", 3.14159265359f);
            SetVar("Rad2Deg", (180.0f / 3.14159265359f));
            SetVar("Deg2Rad", (3.14159265359f / 180.0f));
            SetVar("RandomSeed", randomSeed);
        }

        public Graph TopGraph()
        {
            Graph p = null;

            //parentGraph takes priority!
            if(parentGraph != null)
            {
                return parentGraph;
            }

            if (ParentNode != null)
            {
                p = ParentNode.ParentGraph;

                while(p != null && p is FunctionGraph)
                {
                    var np = (p as FunctionGraph).parentNode;
                    if(np != null)
                    {
                        p = np.ParentGraph;
                    }
                    else
                    {
                        p = null;
                    }
                }
            }

            return p;
        }

        public NodeType? GetOutputType()
        {
            if (OutputNode == null) return null;

            if (OutputNode.Outputs.Count == 0) return null;

            NodeOutput op = OutputNode.Outputs.Find(m => m.Type != NodeType.Execute);

            return op.Type;
        }

        public virtual string GetFunctionShaderCode()
        {
            if (OutputNode == null)
            {
                return "";
            }

            string frag = "";

            List<Node> ordered = OrderNodesForShader();

            //this is in case this function references
            //other functions
            List<Node> calls = Nodes.FindAll(m => m is CallNode);
            for(int i = 0; i < calls.Count; i++)
            {
                CallNode m = (CallNode)calls[i];

                //no need to recreate the function
                //if it is a recursive function!
                if (m.selectedFunction == this)
                {
                    continue;
                }

                string s = m.GetFunctionShaderCode();

                if (string.IsNullOrEmpty(s))
                {
                    return "";
                }

                if (frag.IndexOf(s) == -1)
                {
                    frag += s;
                }
            }

            NodeType? outtype = GetOutputType();

            if (outtype == null)
            {
                return "";
            }

            if(outtype.Value == NodeType.Float4 
                || outtype.Value == NodeType.Color 
                || outtype.Value == NodeType.Gray)
            {
                frag += "vec4 ";
            }
            else if(outtype.Value == NodeType.Float3)
            {
                frag += "vec3 ";
            }
            else if(outtype.Value == NodeType.Float2)
            {
                frag += "vec2 ";
            }
            else if(outtype.Value == NodeType.Float)
            {
                frag += "float ";
            }
            else if(outtype.Value == NodeType.Bool)
            {
                frag += "bool ";
            }
            else
            {
                return "";
            }

            frag += Name.Replace(" ", "").Replace("-", "_") + "(";

            List<Node> args = Nodes.FindAll(m => m is ArgNode);

            for(int i = 0; i < args.Count; i++)
            {
                ArgNode a = (ArgNode)args[i];

                if (a.InputType == NodeType.Float)
                {
                    frag += "float " + a.InputName + ",";
                }
                else if (a.InputType == NodeType.Float2)
                {
                    frag += "vec2 " + a.InputName + ",";
                }
                else if (a.InputType == NodeType.Float3)
                {
                    frag += "vec3 " + a.InputName + ",";
                }
                else if (a.InputType == NodeType.Float4 || a.InputType == NodeType.Color || a.InputType == NodeType.Gray)
                {
                    frag += "vec4 " + a.InputName + ",";
                }
                else if (a.InputType == NodeType.Bool)
                {
                    frag += "bool " + a.InputName + ",";
                }
            }

            if (args.Count > 0)
            {
                frag = frag.Substring(0, frag.Length - 1) + ") {\r\n";
            }
            else
            {
                frag += ") {\r\n";
            }

            string intern = GetInternalShaderCode(ordered, true);

            if (string.IsNullOrEmpty(intern))
            {
                return "";
            }

            frag += intern + "}\r\n\r\n";

            return frag;
        }

        protected List<Node> TravelBranch(Node parent, HashSet<Node> seen)
        {
            List<Node> forward = new List<Node>();
            Queue<Node> queue = new Queue<Node>();

            queue.Enqueue(parent);

            while (queue.Count > 0)
            {
                Node n = queue.Dequeue();

                if (seen.Contains(n))
                {
                    continue;
                }

                seen.Add(n);

                for(int i = 0; i < n.Inputs.Count; i++)
                {
                    NodeInput op = n.Inputs[i];

                    if (op.HasInput)
                    {
                        bool nodeHasExecute = op.Input.Node.Outputs.Find(m => m.Type == NodeType.Execute) != null;

                        if (!nodeHasExecute)
                        {
                            //we don't trigger seen as
                            //it may be shared by another node
                            //further up the chain
                            //this type of node can only be
                            //one of two node types: Get Var
                            //and Constant types
                            //everything else requires
                            //an execute flow
                            forward.Add(op.Input.Node);
                        }
                    }
                }

                forward.Add(n);

                if (n.Outputs.Count > 0)
                {
                    int i = 0;
                    for(i = 0; i < n.Outputs.Count; i++)
                    {
                        NodeOutput op = n.Outputs[i];

                        //we don't care about the actual for loop internals at the momemnt
                        //as each for loop will handle it
                        if (n is ForLoopNode && i == 0)
                        {
                            continue;
                        }

                        if (op.Type == NodeType.Execute)
                        {
                            if (op.To.Count > 1)
                            {
                                //we use recursion if there are multiple links
                                //from one output
                                //otherwise we queue up in queue
                                //and proceed in order
                                for(int j = 0; j < op.To.Count; j++)
                                {
                                    NodeInput t = op.To[j];
                                    var branch = TravelBranch(t.Node, seen);
                                    forward.AddRange(branch);
                                }
                            }
                            else if (op.To.Count > 0)
                            {
                                queue.Enqueue(op.To[0].Node);
                            }
                        }
                    }
                }
            }

            return forward;
        }

        protected List<Node> OrderNodesForShader()
        {
            Stack<Node> reverse = new Stack<Node>();
            Stack<Node> stack = new Stack<Node>();
            List<Node> forward = new List<Node>();

            //oops forgot to check this!
            if(OutputNode == null)
            {
                return forward;
            }

            reverse.Push(OutputNode);

            while (reverse.Count > 0)
            { 
                Node n = reverse.Pop();
                stack.Push(n);

                for(int i = 0; i < n.Inputs.Count; i++)
                {
                    NodeInput op = n.Inputs[i];
                    if (op.HasInput)
                    {
                        if (op.Type == NodeType.Execute)
                        {
                            reverse.Push(op.Input.Node);
                        }
                    }
                }
            }

            HashSet<Node> seen = new HashSet<Node>();
            var sc = stack.ToList();
            stack.Clear();

            var branch = TravelBranch(sc[0], seen);
            forward.AddRange(branch);

            return forward;
        }

        public void UpdateOutputTypes()
        {
            var nodes = OrderNodesForShader();

            for(int i = 0; i < nodes.Count; i++)
            {
                Node n = nodes[i];
                (n as MathNode).UpdateOutputType();
            }
        }

        protected string GetInternalShaderCode(List<Node> nodes, bool asFunc = false)
        {
            if (OutputNode == null)
            {
                return "";
            }

            string sizePart = "vec2 size = vec2(0);\r\n";

            if (parentNode != null)
            {
                Node n = parentNode.TopNode();
                int w = n.Width;
                int h = n.Height;

                sizePart = "vec2 size = vec2(" + w + "," + h + ");\r\n";
            }
            else if(parentGraph != null)
            {
                int w = parentGraph.Width;
                int h = parentGraph.Height;

                sizePart = "vec2 size = vec2(" + w + "," + h + ");\r\n";
            }

            string frag = sizePart
                        + "vec2 pos = UV;\r\n"
                        + GetParentGraphShaderParams();

            for(int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i] as MathNode;

                string d = n.GetShaderPart(frag);

                if (string.IsNullOrEmpty(d))
                {
                    return "";
                }

                if (frag.IndexOf(d) == -1)
                {
                    frag += d;
                }
            }

            var last = OutputNode as MathNode;

            int endIndex = 1;

            //constants and get vars do not have an execute node
            //so their first output starts at 0, rather than 1
            if(last is FloatConstantNode || last is Float2ConstantNode 
                || last is Float3ConstantNode || last is Float4ConstantNode
                || last is BooleanConstantNode || last is GetVarNode)
            {
                endIndex = 0;
            }

            if (!asFunc)
            {
                
                frag += "FragColor = vec4(" + last.ShaderId + endIndex.ToString() + ");\r\n";
            }
            else
            {
                frag += "return " + last.ShaderId + endIndex.ToString() + ";\r\n";
            }

            return frag;
        }

        public virtual bool BuildShader()
        {
            if (OutputNode == null)
            {
                return false;
            }

            if(Shader != null)
            {
                Shader.Release();
                Shader = null;
            }

            List<Node> ordered = OrderNodesForShader();

            string frag = "#version 330 core\r\n"
                         + "out vec4 FragColor;\r\n"
                         + "in vec2 UV;\r\n"
                         + "const float PI = 3.14159265359;\r\n"
                         + "const float Rad2Deg = (180.0 / PI);\r\n"
                         + "const float Deg2Rad = (PI / 180.0);\r\n"
                         + "const float RandomSeed = " + randomSeed + ";\r\n"
                         + "uniform sampler2D Input0;\r\n"
                         + "uniform sampler2D Input1;\r\n"
                         + "uniform sampler2D Input2;\r\n"
                         + "uniform sampler2D Input3;\r\n"
                         + GLSLHash;


            List<Node> calls = Nodes.FindAll(m => m is CallNode);
            for(int i = 0; i < calls.Count; i++)
            {
                CallNode m = (CallNode)calls[i];
                if (m.selectedFunction == this)
                {
                    continue;
                }

                string s = m.GetFunctionShaderCode();

                if (string.IsNullOrEmpty(s))
                {
                    return false;
                }

                if (frag.IndexOf(s) == -1)
                {
                    frag += s;
                }
            }

            frag += "void main() {\r\n";

            string intern = GetInternalShaderCode(ordered);

            if (string.IsNullOrEmpty(intern))
            {
                return false;
            }

            frag += intern + "}";

            //Log.Info("Function Frag Shader Code: {0}", frag);

            //one last check to verify the output actually has the expected output
            if (!HasExpectedOutput)
            {
                return false;
            }

            Shader = Material.Material.CompileFragWithVert("image.glsl", frag);

            if (Shader == null)
            {
                return false;
            }

            return true;
        }

        public virtual void SetOutputNode(string id)
        {
            if(string.IsNullOrEmpty(id))
            {
                OutputNode = null;
                Updated();
                return;
            }

            Node n = null;
            if(NodeLookup.TryGetValue(id, out n))
            {
                OutputNode = n;
                Updated();
            }
        }

        public override bool Add(Node n)
        {
            if(n is MathNode)
            {
                MathNode mn = n as MathNode;
                bool suc = base.Add(n);

                //this handles a case where a node
                //is added from undo / redo / paste
                //for certain nodes such as call node
                if(suc)
                {
                    if(parentNode != null)
                    {
                        mn.ParentNode = parentNode;
                    }
                    else if(parentGraph != null)
                    {
                        mn.OnFunctionParentSet();
                    }
                }

                return suc;
            }
            else if(n is ItemNode)
            {
                return base.Add(n);
            }

            return false;
        }

        //a function graph does not allow embedded graph instances
        //and the type must be coming from MathNodes path
        public override Node CreateNode(string type)
        {
            if (type.Contains("MathNodes") && !type.Contains(System.IO.Path.DirectorySeparatorChar))
            {
                MathNode n = base.CreateNode(type) as MathNode;
                n.ParentNode = parentNode;
                n.ParentGraph = this;
                return n;
            }
            else if(type.Contains("Items") && !type.Contains(System.IO.Path.DirectorySeparatorChar))
            {
                return base.CreateNode(type);
            }

            return null;
        }

        public override void ResizeWith(int width, int height)
        {
            //do nothing in this graph
        }

        public override void ReleaseIntermediateBuffers()
        {
            //do nothing in this graph
        }

        protected void BuildShaderParam(GraphParameterValue param, StringBuilder builder, bool useMinMaxValue = false)
        {
            string type = "";

            if (param.Type == NodeType.Bool)
            {
                type = "bool ";
            }
            else if (param.Type == NodeType.Float)
            {
                type = "float ";
            }
            else if (param.Type == NodeType.Color || param.Type == NodeType.Float4 || param.Type == NodeType.Gray)
            {
                type = "vec4 ";
            }
            else if (param.Type == NodeType.Float2)
            {
                type = "vec2 ";
            }
            else if (param.Type == NodeType.Float3)
            {
                type = "vec3 ";
            }
            else
            {
                return;
            }

            string prefix = "p_";
            string s1 = prefix + param.Name.Replace(" ", "").Replace("-", "") + " = ";

            builder.Append(type);
            builder.Append(s1);

            if (param.IsFunction())
            {
                BuildShaderFunctionValue(param, builder);
            }
            else
            {
                BuildShaderParamValue(param, builder, useMinMaxValue);
            }
        }

        protected void BuildShaderParamValue(GraphParameterValue param, StringBuilder builder, bool useMinMaxValue = false)
        {
            if (param.Type == NodeType.Bool)
            {
                builder.Append(Convert.ToBoolean(param.Value).ToString().ToLower() + ";\r\n");
            }
            else if (param.Type == NodeType.Float)
            {
                builder.Append(param.FloatValue.ToString() + ";\r\n");
            }
            else if (param.Type == NodeType.Float4 || param.Type == NodeType.Gray || param.Type == NodeType.Color)
            {
                MVector vec = new MVector();

                if (param.Value is MVector)
                {
                    if (!useMinMaxValue)
                    {
                        vec = (MVector)param.Value;
                    }
                    else
                    {
                        vec = param.VectorValue;
                    }
                }

                builder.Append("vec4(" + vec.X + "," + vec.Y + "," + vec.Z + "," + vec.W + ");\r\n");
            }
            else if (param.Type == NodeType.Float2)
            {
                MVector vec = new MVector();

                if (param.Value is MVector)
                {
                    if (!useMinMaxValue)
                    {
                        vec = (MVector)param.Value;
                    }
                    else
                    {
                        vec = param.VectorValue;
                    }
                }

                builder.Append("vec2(" + vec.X + "," + vec.Y + ");\r\n");
            }
            else if (param.Type == NodeType.Float3)
            {
                MVector vec = new MVector();

                if (param.Value is MVector)
                {
                    if (!useMinMaxValue)
                    {
                        vec = (MVector)param.Value;
                    }
                    else
                    {
                        vec = param.VectorValue;
                    }
                }

                builder.Append("vec3(" + vec.X + "," + vec.Y + "," + vec.Z + ");\r\n");
            }
        }

        protected void BuildShaderFunctionValue(GraphParameterValue param, StringBuilder builder)
        {
            FunctionGraph fn = param.Value as FunctionGraph;
            fn.TryAndProcess();
            object value = fn.Result;

            if (param.Type == NodeType.Bool)
            {
                builder.Append(Convert.ToBoolean(param.Value).ToString().ToLower() + ";\r\n");
            }
            else if (param.Type == NodeType.Float)
            {
                builder.Append(Convert.ToSingle(value).ToString() + ";\r\n");
            }
            else if (param.Type == NodeType.Float4 || param.Type == NodeType.Gray || param.Type == NodeType.Color)
            {
                MVector vec = new MVector();

                if (value is MVector)
                {
                    vec = (MVector)value;
                }

                builder.Append("vec4(" + vec.X + "," + vec.Y + "," + vec.Z + "," + vec.W + ");\r\n");
            }
            else if (param.Type == NodeType.Float2)
            {
                MVector vec = new MVector();

                if (value is MVector)
                {
                    vec = (MVector)value;
                }

                builder.Append("vec2(" + vec.X + "," + vec.Y + ");\r\n");
            }
            else if (param.Type == NodeType.Float3)
            {
                MVector vec = new MVector();

                if (value is MVector)
                {
                    vec = (MVector)value;
                }

                builder.Append("vec3(" + vec.X + "," + vec.Y + "," + vec.Z + ");\r\n");
            }
        }

        protected string GetParentGraphShaderParams()
        {
            StringBuilder builder = new StringBuilder();

            try
            {

                if (parentNode != null)
                {
                    var p = TopGraph();

                    if (p != null)
                    {
                        foreach (var param in p.Parameters.Values)
                        {
                            BuildShaderParam(param, builder);
                        }

                        for(int i = 0; i < p.CustomParameters.Count; i++)
                        {
                            GraphParameterValue param = p.CustomParameters[i];
                            if (!param.IsFunction())
                            {
                                BuildShaderParam(param, builder, true);
                            }
                        }
                    }
                }
            }
            catch (StackOverflowException e)
            {
                Log.Error(e);
                Log.Error("There is an infinite function reference loop in promoted graph parameters.");
                return "";
            }

            return builder.ToString();
        }

        protected void SetParentGraphVars(Graph g)
        {
            if (g == null) return;

            try
            {
                var p = g;

                if (p != null)
                {
                    foreach (var k in p.Parameters.Keys)
                    {
                        var param = p.Parameters[k];

                        if (!param.IsFunction())
                        {
                            SetVar("p_" + param.Name.Replace(" ", "").Replace("-", ""), param.Value);
                        }
                    }

                    for(int i = 0; i < p.CustomParameters.Count; i++)
                    {
                        GraphParameterValue param = p.CustomParameters[i];

                        if (!param.IsFunction())
                        {
                            SetVar("p_" + param.Name.Replace(" ", "").Replace("-", ""), param.Value);
                        }
                    }
                }
            }
            catch (StackOverflowException e)
            {
                //possible
                Log.Error(e);
                Log.Error("There is an infinite function reference loop in promoted graph parameters.");
            }
        }

        protected void SetParentNodeVars(Graph g)
        {
            try
            {
                if (g == null || parentNode == null) return;

                var props = parentNode.GetType().GetProperties();

                var p = g;

                if (p != null)
                {
                    for(int i = 0; i < props.Length; i++)
                    {
                        var prop = props[i];

                        if (!prop.PropertyType.Equals(typeof(int))
                            && !prop.PropertyType.Equals(typeof(float))
                            && !prop.PropertyType.Equals(typeof(MVector))
                            && !prop.PropertyType.Equals(typeof(bool))
                            && !prop.PropertyType.Equals(typeof(double))
                            && !prop.PropertyType.Equals(typeof(Vector4)))
                        {
                            continue;
                        }

                        try
                        {
                            HidePropertyAttribute hb = prop.GetCustomAttribute<HidePropertyAttribute>();

                            if (hb != null)
                            {
                                continue;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }

                        object v = null;
                        string varName = "";

                        if (p.HasParameterValue(parentNode.Id, prop.Name))
                        {
                            var gp = p.GetParameterRaw(parentNode.Id, prop.Name);
                            if (!gp.IsFunction())
                            {
                                v = gp.Value;
                            }
                        }
                        else
                        {
                            v = prop.GetValue(parentNode);
                        }

                        try
                        {
                            TitleAttribute t = prop.GetCustomAttribute<TitleAttribute>();

                            if (t != null)
                            {
                                varName = t.Title.Replace(" ", "").Replace("-", "");
                            }
                            else
                            {
                                varName = prop.Name;
                            }
                        }
                        catch
                        {
                            varName = prop.Name;
                        }

                        if (v != null)
                        {
                            if (v is Vector4)
                            {
                                Vector4 vec = (Vector4)v;
                                v = new MVector(vec.X, vec.Y, vec.Z, vec.W);
                            }

                            SetVar(varName, v);
                        }
                    }
                }
                else
                {
                    for(int i = 0; i < props.Length; i++)
                    {
                        var prop = props[i];
                        if (!prop.PropertyType.Equals(typeof(int))
                            && !prop.PropertyType.Equals(typeof(float))
                            && !prop.PropertyType.Equals(typeof(MVector))
                            && !prop.PropertyType.Equals(typeof(bool))
                            && !prop.PropertyType.Equals(typeof(double))
                            && !prop.PropertyType.Equals(typeof(Vector4)))
                        {
                            continue;
                        }

                        try
                        {
                            HidePropertyAttribute hb = prop.GetCustomAttribute<HidePropertyAttribute>();

                            if (hb != null)
                            {
                                continue;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }

                        object v = prop.GetValue(parentNode);
                        string varName = "";

                        try
                        {
                            TitleAttribute t = prop.GetCustomAttribute<TitleAttribute>();

                            if (t != null)
                            {
                                varName = t.Title.Replace(" ", "").Replace("-", "");
                            }
                            else
                            {
                                varName = prop.Name;
                            }
                        }
                        catch
                        {
                            varName = prop.Name;
                        }

                        if (v != null)
                        {
                            if (v is Vector4)
                            {
                                Vector4 vec = (Vector4)v;
                                v = new MVector(vec.X, vec.Y, vec.Z, vec.W);
                            }

                            SetVar(varName, v);
                        }
                    }
                }
            }
            catch (StackOverflowException e)
            {
                Log.Error(e);
                //stackoverflow possible if you do a loop of function parameter values
                Log.Error("There is an infinite function reference loop between node parameters");
            }
        }

        public override void TryAndProcess()
        {
            //if (!HasExpectedOutput) return;

            //small optimization
            var top = TopGraph();

            SetParentNodeVars(top);
            SetParentGraphVars(top);

            if(parentNode != null)
            {
                var n = parentNode.TopNode();
                int w = n.Width;
                int h = n.Height;

                SetVar("size", new MVector(w, h));
            }
            else if(parentGraph != null)
            {
                SetVar("size", new MVector(parentGraph.Width, parentGraph.Height));
            }
            else
            {
                SetVar("size", new MVector());
            }

            if (OutputNode == null) return;

            List<Node> ordered = OrderNodesForShader();
            //this ensures the function graph is processed
            //in the proper order
            //just as if it was running in the shader code
            if(ordered.Count > 0)
            {
                for(int i = 0; i < ordered.Count; i++)
                {
                    ordered[i].TryAndProcess();
                } 
            }
        }

        public class FunctionGraphData : GraphData
        {
            public string outputNode;
        }

        public override string GetJson()
        {
            FunctionGraphData d = new FunctionGraphData();

            List<string> data = new List<string>();

            for(int i = 0; i < Nodes.Count; i++)
            {
                Node n = Nodes[i];
                data.Add(n.GetJson());
            }

            d.name = Name;
            d.nodes = data;
            d.outputs = new List<string>();
            d.inputs = new List<string>();

            d.outputNode = OutputNode != null ? OutputNode.Id : null;

            return JsonConvert.SerializeObject(d);
        }

        public override void FromJson(string data)
        {
            base.FromJson(data);

            FunctionGraphData d = JsonConvert.DeserializeObject<FunctionGraphData>(data);

            Node n = null;

            if (d.outputNode != null)
            {
                NodeLookup.TryGetValue(d.outputNode, out n);
                OutputNode = n;
            }
        }

        public override void Dispose()
        {
            if(Shader != null)
            {
                Shader.Release();
                Shader = null;
            }

            base.Dispose();
        }
    }
}
