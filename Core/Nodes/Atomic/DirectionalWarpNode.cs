﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Materia.Textures;
using Materia.Imaging.GLProcessing;
using Newtonsoft.Json;
using Materia.Nodes.Attributes;
using Materia.Nodes.Helpers;

namespace Materia.Nodes.Atomic
{
    public class DirectionalWarpNode : ImageNode
    {
        protected DirectionalWarpProcessor processor;
        protected BlurProcessor blur;

        protected NodeInput input;
        protected NodeInput input1;

        protected NodeOutput output;

        protected float angle;
        [Promote(NodeType.Float)]
        [Editable(ParameterInputType.IntSlider, "Angle", "Default", 0, 360)]
        public float Angle
        {
            get
            {
                return angle;
            }
            set
            {
                angle = value;
                TriggerValueChange();
            }
        }

        protected float intensity;
        [Promote(NodeType.Float)]
        [Editable(ParameterInputType.FloatSlider, "Intensity")]
        public float Intensity
        {
            get
            {
                return intensity;
            }
            set
            {
                intensity = value;
                TriggerValueChange();
            }
        }

        protected float blurIntensity;
        [Promote(NodeType.Float)]
        [Editable(ParameterInputType.IntSlider, "Blur Intensity", "Default", 0, 128)]
        public float BlurIntensity
        {
            get
            {
                return blurIntensity;
            }
            set
            {
                blurIntensity = value;
                TriggerValueChange();
            }
        }

        public DirectionalWarpNode(int w, int h, GraphPixelType p = GraphPixelType.RGBA) : base()
        {
            Name = "Directional Warp";
            Id = Guid.NewGuid().ToString();

            width = w;
            height = h;

            intensity = 1;

            tileX = tileY = 1;

            angle = 0;

            previewProcessor = new BasicImageRenderer();
            processor = new DirectionalWarpProcessor();
            blur = new BlurProcessor();

            internalPixelType = p;

            input = new NodeInput(NodeType.Gray | NodeType.Color, this, "Image Input");
            input1 = new NodeInput(NodeType.Gray, this, "Grayscale Gradients");

            output = new NodeOutput(NodeType.Gray | NodeType.Color, this);

            Inputs.Add(input);
            Inputs.Add(input1);
            Outputs.Add(output);
        }

        private void GetParams()
        {
            if (!input.HasInput || !input1.HasInput) return;

            pintensity = intensity;
            bintensity = blurIntensity;
            pangle = angle;

            if (ParentGraph != null && ParentGraph.HasParameterValue(Id, "Intensity"))
            {
                pintensity = Utils.ConvertToFloat(ParentGraph.GetParameterValue(Id, "Intensity"));
            }

            if (ParentGraph != null && ParentGraph.HasParameterValue(Id, "BlurIntensity"))
            {
                bintensity = Utils.ConvertToFloat(ParentGraph.GetParameterValue(Id, "BlurIntensity"));
            }

            if (ParentGraph != null && ParentGraph.HasParameterValue(Id, "Angle"))
            {
                pangle = Utils.ConvertToFloat(ParentGraph.GetParameterValue(Id, "Angle"));
            }
        }

        public override void TryAndProcess()
        {
            GetParams();
            Process();
        }

        float bintensity;
        float pintensity;
        float pangle;
        void Process()
        {
            if (!input.HasInput || !input1.HasInput) return;

            GLTextuer2D i1 = (GLTextuer2D)input.Reference.Data;
            GLTextuer2D i2 = (GLTextuer2D)input1.Reference.Data;

            if (i1 == null) return;
            if (i1.Id == 0) return;

            if (i2 == null) return;
            if (i2.Id == 0) return;

            CreateBufferIfNeeded();

            if (processor == null || blur == null) return;

            processor.TileX = tileX;
            processor.TileY = TileY;
            processor.Angle = pangle * (float)(Math.PI / 180.0f);
            processor.Intensity = pintensity;
            processor.Process(width, height, i1, i2, buffer);
            processor.Complete();

            blur.Intensity = Math.Max(0, bintensity);
            blur.Process(width, height, buffer, buffer);
            blur.Complete();

            output.Data = buffer;
            TriggerTextureChange();
        }

        public class WarpData : NodeData
        {
            public float intensity;
            public float blurIntensity;
            public float angle;
        }

        public override string GetJson()
        {
            WarpData d = new WarpData();
            FillBaseNodeData(d);
            d.intensity = intensity;
            d.blurIntensity = blurIntensity;
            d.angle = angle;

            return JsonConvert.SerializeObject(d);
        }

        public override void FromJson(string data)
        {
            WarpData d = JsonConvert.DeserializeObject<WarpData>(data);
            SetBaseNodeDate(d);
            intensity = d.intensity;
            blurIntensity = d.blurIntensity;
            angle = d.angle;
        }

        public override void Dispose()
        {
            base.Dispose();

            if (processor != null)
            {
                processor.Release();
                processor = null;
            }

            if (blur != null)
            {
                blur.Release();
                blur = null;
            }
        }
    }
}
