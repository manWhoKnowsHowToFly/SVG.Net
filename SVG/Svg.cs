using System.Drawing;
using System.IO;
using System.Xml.Linq;

namespace SVG {
    public class Svg {
        Parser parserCore = new Parser();
        RenderingEngine renderingEngineCore = new RenderingEngine();
        XDocument svgDocumentCore;
        protected XDocument Document {
            get { return svgDocumentCore; }
            set { svgDocumentCore = value; }
        }
        protected Parser Parser { get { return parserCore; } }
        protected RenderingEngine RenderingEngine { get { return renderingEngineCore; } }
        public static Svg FromFile(string filePath) {
            Stream stream = new FileStream(filePath, FileMode.Open);
            XDocument document = XDocument.Load(stream);
            Svg svg = new Svg();
            svg.Document = document;
            return svg;
        }
        public void Draw(Graphics g) {
            if(!Parser.IsReady) {
                Parser.Parse(Document);
                RenderingEngine.Render(Parser);
            }
            RenderingEngine.Draw(g);
        }
    }
}