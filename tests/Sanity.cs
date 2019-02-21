using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace triaxis.Runtime.DynamicDispatchTests
{
    [TestClass]
    public class Sanity
    {
        class Node { }

        class ValueNode : Node
        {
            public ValueNode(int value)
            {
                this.Value = value;
            }

            public int Value { get; set; }
        }

        class BinaryNode : Node
        {
            public Node Left { get; set; }
            public Node Right { get; set; }
        }

        class Writer
        {
            TextWriter output;

            public Writer(TextWriter output)
            {
                this.output = output;
            }

            public void Write(BinaryNode node, Action<Node> next)
            {
                next(node.Left);
                output.Write(",");
                next(node.Right);
            }

            public void Write(ValueNode node)
            {
                Write(node.Value);
            }

            public void Write(int value)
            {
                output.Write(value);
            }
        }

        class MultiplyWriter : Writer
        {
            int multiply;

            public MultiplyWriter(TextWriter writer, int multiply)
                : base(writer)
            {
                this.multiply = multiply;
            }

            public new void Write(ValueNode node)
            {
                Console.WriteLine(new System.Diagnostics.StackTrace());

                base.Write(node.Value * 2);
            }
        }

        [TestMethod]
        public void DispatchTest()
        {
            var ddWrite = new DynamicDispatch<Writer, Node>("Write");
            var ddMulWrite = new DynamicDispatch<MultiplyWriter, Node>("Write");

            var tree = new BinaryNode
            {
                Left = new BinaryNode
                {
                    Left = new ValueNode(3),
                    Right = new BinaryNode {
                        Left = new ValueNode(4),
                        Right = new ValueNode(6)
                    }
                },
                Right = new ValueNode(8)
            };

            var sw = new StringWriter();
            ddWrite.Dispatch(new Writer(sw), tree);
            Assert.AreEqual("3,4,6,8", sw.ToString());

            sw = new StringWriter();
            ddMulWrite.Dispatch(new MultiplyWriter(sw, 2), tree);
            Assert.AreEqual("6,8,12,16", sw.ToString());
        }
    }
}
