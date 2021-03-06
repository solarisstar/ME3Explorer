﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Be.Windows.Forms;
using ME1Explorer.Unreal;
using ME1Explorer.Unreal.Classes;
using System.Diagnostics;

namespace ME1Explorer.Interpreter2
{
    public partial class Interpreter2 : Form
    {
        public PCCObject pcc;
        public int Index;
        public byte[] memory;
        public int memsize;
        public int readerpos;
        private int previousArrayView = -1; //-1 means it has not been previously set
        public struct PropHeader
        {
            public int name;
            public int type;
            public int size;
            public int index;
            public int offset;
        }

        public string[] Types =
        {
            "StructProperty", //0
            "IntProperty",
            "FloatProperty",
            "ObjectProperty",
            "NameProperty",
            "BoolProperty",  //5
            "ByteProperty",
            "ArrayProperty",
            "StrProperty",
            "StringRefProperty",
            "DelegateProperty"//10
        };

        public const int STRUCT_PROPERTY = 0;
        public const int INT_PROPERTY = 1;
        public const int FLOAT_PROPERTY = 2;
        public const int OBJECT_PROPERTY = 3;
        public const int NAME_PROPERTY = 4;
        public const int BOOL_PROPERTY = 5;
        public const int BYTE_PROPERTY = 6;
        public const int ARRAY_PROPERTY = 7;
        public const int STRING_PROPERTY = 8;
        public const int STRINGREF_PROPERTY = 9;
        public const int DELEGATE_PROPERTY = 10;

        public const int TOPLEVEL_TAG = -1; //indicates this is a top level object in the tree
        public const int ARRAYLEAF_TAG = -2; //indicates this is a generic leaf in an arraylist, with no defined type
        public const int STRUCTLEAFBYTE_TAG = -3; //indicates this is a leaf of a StructProperty that is a byte
        public const int STRUCTLEAFFLOAT_TAG = -4; //indicates this is a leaf of a StructProperty that is a float
        public const int STRUCTLEAFDEG_TAG = -5; //indicates this is a leaf of a StructProperty that is in degrees (actually unreal rotation units)
        public const int NONARRAYLEAF_TAG = -100; //indicates this is not an array leaf but does not specify what it is (e.g. could be unknown.)

        private const int ARRAYSVIEW_RAW = 0;
        private const int ARRAYSVIEW_IMPORTEXPORT = 1;
        private const int ARRAYSVIEW_NAMES = 2;

        private BioTlkFileSet tlkset;
        private int lastSetOffset = -1; //offset set by program, used for checking if user changed since set 
        private int LAST_SELECTED_PROP_TYPE = -100; //last property type user selected. Will use to check the current offset for type
        private TreeNode LAST_SELECTED_NODE = null; //last selected tree node

        public Interpreter2()
        {
            InitializeComponent();
            arrayViewerDropdown.SelectedIndex = 0;
        }

        public void InitInterpreter(BioTlkFileSet editorTalkset = null)
        {
            DynamicByteProvider db = new DynamicByteProvider(pcc.Exports[Index].Data);
            hb1.ByteProvider = db;
            memory = pcc.Exports[Index].Data;
            memsize = memory.Length;

            // attempt to find a TlkFileSet associated with the object, else just pick the first one and hope it's correct
            if (editorTalkset == null)
            {
                PropertyReader.Property tlkSetRef = PropertyReader.getPropList(pcc, pcc.Exports[Index].Data).FirstOrDefault(x => pcc.getNameEntry(x.Name) == "m_oTlkFileSet");
                if(tlkSetRef != null)
                {
                    tlkset = new BioTlkFileSet(pcc, tlkSetRef.Value.IntValue - 1);
                }
                else
                {
                    tlkset = new BioTlkFileSet(pcc);
                }
            }
            else
            {
                tlkset = editorTalkset;
            }
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            StartScan();
        }

        public new void Show()
        {
            base.Show();
            toolStripStatusLabel1.Text = "Class: " + pcc.Exports[Index].ClassName + ", Index: " + Index;
            toolStripStatusLabel2.Text = "@" + Path.GetFileName(pcc.pccFileName);
            StartScan();
        }

        private void StartScan()
        {
            treeView1.Nodes.Clear();
            readerpos = PropertyReader.detectStart(pcc, memory, pcc.Exports[Index].ObjectFlags);
            BitConverter.IsLittleEndian = true;
            List<PropHeader> topLevelHeaders = ReadHeadersTillNone();
            TreeNode topLevelTree = new TreeNode("0000 : " + pcc.Exports[Index].ObjectName);
            topLevelTree = GenerateTree(topLevelTree, topLevelHeaders);
            topLevelTree.Tag = TOPLEVEL_TAG;
            treeView1.Nodes.Add(topLevelTree);
            treeView1.CollapseAll();
            treeView1.Nodes[0].Expand();
        }

        public TreeNode Scan()
        {
            readerpos = PropertyReader.detectStart(pcc, memory, pcc.Exports[Index].ObjectFlags);
            BitConverter.IsLittleEndian = true;
            List<PropHeader> topLevelHeaders = ReadHeadersTillNone();
            TreeNode t = new TreeNode("0000 : " + pcc.Exports[Index].ObjectName);
            return GenerateTree(t, topLevelHeaders);
        }

        public TreeNode GenerateTree(TreeNode input, List<PropHeader> headersList)
        {
            TreeNode ret = input;
            foreach (PropHeader header in headersList)
            {
                int type = getType(pcc.getNameEntry(header.type));
                if (type != ARRAY_PROPERTY && type != STRUCT_PROPERTY)
                    ret.Nodes.Add(GenerateNode(header));
                else
                {
                    if (type == ARRAY_PROPERTY)
                    {
                        TreeNode t = GenerateNode(header);
                        int arrayLength = BitConverter.ToInt32(memory, header.offset + 24);
                        readerpos = header.offset + 28;
                        int tmp = readerpos;
                        List<PropHeader> propHeaders = ReadHeadersTillNone();
                        if (propHeaders.Count != 0 && arrayLength > 0 && header.size > 24)
                        {
                            if (arrayLength == 1)
                            {
                                readerpos = tmp;
                                t = GenerateTree(t, propHeaders);
                            }
                            else
                            {
                                for (int i = 0; i < arrayLength; i++)
                                {
                                    readerpos = tmp;
                                    List<PropHeader> arrayListPropHeaders = ReadHeadersTillNone();
                                    tmp = readerpos;
                                    TreeNode n = new TreeNode(i.ToString());
                                    n = GenerateTree(n, arrayListPropHeaders);
                                    t.Nodes.Add(n);
                                }
                            }
                            ret.Nodes.Add(t);
                        }
                        else
                        {
                            for (int i = 0; i < (header.size - 4) / 4; i++)
                            {
                                int val = BitConverter.ToInt32(memory, header.offset + 28 + i * 4);
                                string s = (header.offset + 28 + i * 4).ToString("X4") + "|";
                                TreeNode node = new TreeNode();
                                node.Name = (header.offset + 28 + i * 4).ToString();
                                if (arrayViewerDropdown.SelectedIndex == ARRAYSVIEW_IMPORTEXPORT)
                                {
                                    s += i + ": ";
                                    Debug.WriteLine("IMPEXP BLOCK REACHED.");
                                    int value = val;
                                    if (value == 0)
                                    {
                                        //invalid
                                        s += "Null [" + value + "] ";
                                    }
                                    else
                                    {

                                        bool isImport = value < 0;
                                        if (isImport)
                                        {
                                            value = -value;
                                        }
                                        value--; //0-indexed
                                        if (isImport)
                                        {
                                            if (pcc.Imports.Count > value)
                                            {
                                                s += pcc.Imports[value].ObjectName + " [IMPORT " + value + "]";
                                            }
                                            else
                                            {
                                                s += "Index not in import list [" + value + "]";
                                            }
                                        }
                                        else
                                        {
                                            if (pcc.Exports.Count > value)
                                            {
                                                s += pcc.Exports[value].ObjectName + " [EXPORT " + value + "]";
                                            }
                                            else
                                            {
                                                s += "Index not in export list [" + value + "]";
                                            }
                                        }
                                    }
                                }
                                else if (arrayViewerDropdown.SelectedIndex == ARRAYSVIEW_NAMES)
                                {
                                    s += i / 2 + ": ";
                                    Debug.WriteLine("NAMES BLOCK REACHED.");
                                    int value = val;
                                    if (value < 0)
                                    {
                                        //invalid
                                        s += "Invalid Name Index [" + value + "]";
                                    }
                                    else
                                    {
                                        if (pcc.Names.Count > value)
                                        {
                                            s += pcc.Names[value] + " [NAMEINDEX " + value + "]";
                                        }
                                        else
                                        {
                                            s += "Index not in name list [" + value + "]";
                                        }
                                    }
                                    i++; //names are 8 bytes so skip an entry
                                }
                                else
                                {
                                    s += i + ": ";
                                    s += val.ToString();
                                }
                                node.Text = s;
                                node.Tag = ARRAYLEAF_TAG;
                                t.Nodes.Add(node);
                            }
                            ret.Nodes.Add(t);
                        }
                    }
                    if (type == STRUCT_PROPERTY)
                    {
                        TreeNode t = GenerateNode(header);
                        int name = BitConverter.ToInt32(memory, header.offset + 24);
                        readerpos = header.offset + 32;
                        List<PropHeader> ll = ReadHeadersTillNone();
                        if (ll.Count != 0)
                        {
                            t = GenerateTree(t, ll);
                            ret.Nodes.Add(t);
                        }
                        else
                        {
                            TreeNode node;
                            int pos;
                            string structType = pcc.getNameEntry(name);
                            if (structType == "Vector")
                            {
                                string[] labels = { "X", "Y", "Z" };
                                for (int i = 0; i < 3; i++)
                                {
                                    pos = readerpos + (i * 4);
                                    node = new TreeNode(pos.ToString("X4") + " : " + labels[i] + " : " + BitConverter.ToSingle(memory, pos));
                                    node.Name = pos.ToString();
                                    node.Tag = STRUCTLEAFFLOAT_TAG;
                                    t.Nodes.Add(node);
                                }
                            }
                            else if (structType == "Rotator")
                            {
                                string[] labels = { "Pitch", "Yaw", "Roll" };
                                int val;
                                for (int i = 0; i < 3; i++)
                                {
                                    pos = readerpos + (i * 4);
                                    val = BitConverter.ToInt32(memory, pos);
                                    node = new TreeNode(pos.ToString("X4") + " : " + labels[i] + " : " + val + " (" + ((float)val * 360f / 65536f) + " degrees)");
                                    node.Name = pos.ToString();
                                    node.Tag = STRUCTLEAFDEG_TAG;
                                    t.Nodes.Add(node);
                                }
                            }
                            else if (structType == "Color")
                            {
                                string[] labels = { "B", "G", "R", "A" };
                                for (int i = 0; i < 4; i++)
                                {
                                    pos = readerpos + i;
                                    node = new TreeNode(pos.ToString("X4") + " : " + labels[i] + " : " + memory[pos]);
                                    node.Name = pos.ToString();
                                    node.Tag = STRUCTLEAFBYTE_TAG;
                                    t.Nodes.Add(node);
                                }
                            }
                            else if (structType == "LinearColor")
                            {
                                string[] labels = { "R", "G", "B", "A" };
                                for (int i = 0; i < 4; i++)
                                {
                                    pos = readerpos + (i * 4);
                                    node = new TreeNode(pos.ToString("X4") + " : " + labels[i] + " : " + BitConverter.ToSingle(memory, pos));
                                    node.Name = pos.ToString();
                                    node.Tag = STRUCTLEAFFLOAT_TAG;
                                    t.Nodes.Add(node);
                                }
                            }
                            else
                            {
                                for (int i = 0; i < header.size / 4; i++)
                                {
                                    int val = BitConverter.ToInt32(memory, header.offset + 32 + i * 4);
                                    string s = (header.offset + 32 + i * 4).ToString("X4") + " : " + val.ToString();
                                    t.Nodes.Add(s);
                                }
                            }
                            ret.Nodes.Add(t);
                        }
                    }

                }
            }
            return ret;
        }



        public TreeNode GenerateNode(PropHeader p)
        {
            string s = p.offset.ToString("X4") + " : ";
            s += "Name: \"" + pcc.getNameEntry(p.name) + "\" ";
            s += "Type: \"" + pcc.getNameEntry(p.type) + "\" ";
            s += "Size: " + p.size.ToString() + " Value: ";
            int propertyType = getType(pcc.getNameEntry(p.type));
            int idx;
            byte val;
            switch (propertyType)
            {
                case INT_PROPERTY:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += idx.ToString();
                    break;
                case OBJECT_PROPERTY:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += idx.ToString() + " (" + pcc.getObjectName(idx) + ")";
                    break;
                case STRING_PROPERTY:
                    int count = BitConverter.ToInt32(memory, p.offset + 24);
                    s += "\"";
                    for (int i = 0; i < count - 1; i++)
                        s += (char)memory[p.offset + 28 + i];
                    s += "\"";
                    break;
                case BOOL_PROPERTY:
                    val = memory[p.offset + 24];
                    s += (val == 1).ToString();
                    break;
                case FLOAT_PROPERTY:
                    float f = BitConverter.ToSingle(memory, p.offset + 24);
                    s += f.ToString() + "f";
                    break;
                case STRUCT_PROPERTY:
                case NAME_PROPERTY:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += "\"" + pcc.getNameEntry(idx) + "\"";
                    break;
                case BYTE_PROPERTY:
                    if (p.size == 1)
                    {
                        val = memory[p.offset + 32];
                        s += val.ToString();
                    }
                    else
                    {
                        idx = BitConverter.ToInt32(memory, p.offset + 24);
                        s += "\"" + pcc.getNameEntry(idx) + "\"";
                    }
                    break;
                case ARRAY_PROPERTY:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += idx.ToString() + "(count)";
                    break;
                case STRINGREF_PROPERTY:
                    idx = BitConverter.ToInt32(memory, p.offset + 24);
                    s += "#" + idx.ToString() + ": ";
                    s += tlkset == null ? "(.tlk not loaded)" : tlkset.findDataById(idx);
                    break;
            }
            TreeNode ret = new TreeNode(s);
            ret.Tag = propertyType;
            ret.Name = p.offset.ToString();
            return ret;
        }

        public int getType(string s)
        {
            int ret = -1;
            for (int i = 0; i < Types.Length; i++)
                if (s == Types[i])
                    ret = i;
            return ret;
        }

        public List<PropHeader> ReadHeadersTillNone()
        {
            List<PropHeader> ret = new List<PropHeader>();
            bool run = true;
            while (run)
            {
                PropHeader p = new PropHeader();
                p.name = BitConverter.ToInt32(memory, readerpos);
                if (!pcc.isName(p.name))
                    run = false;
                else
                {
                    if (pcc.getNameEntry(p.name) != "None")
                    {
                        p.type = BitConverter.ToInt32(memory, readerpos + 8);
                        if (!pcc.isName(p.type) || getType(pcc.getNameEntry(p.type)) == -1 || BitConverter.ToInt32(memory, readerpos + 4) != 0)
                            run = false;
                        else
                        {
                            p.size = BitConverter.ToInt32(memory, readerpos + 16);
                            p.index = BitConverter.ToInt32(memory, readerpos + 20);
                            p.offset = readerpos;
                            ret.Add(p);
                            readerpos += p.size + 24;
                            if (getType(pcc.getNameEntry(p.type)) == BOOL_PROPERTY)
                                readerpos += 4;
                            if (getType(pcc.getNameEntry(p.type)) == STRUCT_PROPERTY)
                                readerpos += 8;
                        }
                    }
                    else
                    {
                        p.type = p.name;
                        p.size = 0;
                        p.index = 0;
                        p.offset = readerpos;
                        ret.Add(p);
                        readerpos += 8;
                        run = false;
                    }
                }
            }
            return ret;
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = "*.txt|*.txt";
            d.FileName = pcc.Exports[Index].ObjectName + ".txt";
            if (d.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(d.FileName, FileMode.Create, FileAccess.Write);
                PrintNodes(treeView1.Nodes, fs, 0);
                fs.Close();
                MessageBox.Show("Done.");
            }
        }

        public void PrintNodes(TreeNodeCollection t, FileStream fs, int depth)
        {
            string tab = "";
            for (int i = 0; i < depth; i++)
                tab += ' ';
            foreach (TreeNode t1 in t)
            {
                string s = tab + t1.Text;
                WriteString(fs, s);
                fs.WriteByte(0xD);
                fs.WriteByte(0xA);
                if (t1.Nodes.Count != 0)
                    PrintNodes(t1.Nodes, fs, depth + 4);
            }
        }

        public void WriteString(FileStream fs, string s)
        {
            for (int i = 0; i < s.Length; i++)
                fs.WriteByte((byte)s[i]);
        }

        public int CharToInt(char c)
        {
            int r = -1;
            string signs = "0123456789ABCDEF";
            for (int i = 0; i < signs.Length; i++)
                if (signs[i] == c)
                    r = i;
            return r;
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            LAST_SELECTED_NODE = e.Node;
            proptext.Visible = setPropertyButton.Visible = setValueSeparator.Visible = enumDropdown.Visible = false;
            if (e.Node.Name == "")
            {
                Debug.WriteLine("This node is not parsable.");
                //can't attempt to parse this.
                addArrayElementButton.Visible = false;
                deleteArrayElement.Visible = false;
                arrayPropertyDropdown.Enabled = false;
                LAST_SELECTED_PROP_TYPE = NONARRAYLEAF_TAG;
                return;
            }
            try
            {
                int off = Convert.ToInt32(e.Node.Name);
                hb1.SelectionStart = off;
                lastSetOffset = off;
                hb1.SelectionLength = 1;
                Debug.WriteLine("Node offset: " + off);
                if (e.Node.Tag != null && e.Node.Tag.Equals(ARRAYLEAF_TAG))
                {
                    addArrayElementButton.Visible = false;
                    TryParseArrayProperty();
                    LAST_SELECTED_PROP_TYPE = ARRAYLEAF_TAG;
                }
                else if (e.Node.Tag != null && (e.Node.Tag.Equals(STRUCTLEAFFLOAT_TAG) || e.Node.Tag.Equals(STRUCTLEAFBYTE_TAG) || e.Node.Tag.Equals(STRUCTLEAFDEG_TAG)))
                {
                    addArrayElementButton.Visible = false;
                    deleteArrayElement.Visible = false;
                    arrayPropertyDropdown.Enabled = false;
                    TryParseStructProperty((int)e.Node.Tag);
                    LAST_SELECTED_PROP_TYPE = (int)e.Node.Tag;
                }
                else if (e.Node.Tag != null && e.Node.Tag.Equals(ARRAY_PROPERTY))
                {
                    addArrayElementButton.Visible = true;
                    deleteArrayElement.Visible = false;
                    arrayPropertyDropdown.Enabled = false;
                    proptext.Clear();
                    setPropertyButton.Visible = false;
                    setValueSeparator.Visible = true;
                    LAST_SELECTED_PROP_TYPE = ARRAY_PROPERTY;
                    //probably an array of names or import/export references
                    if (e.Node.GetNodeCount(false) == 0 || (e.Node.FirstNode?.Tag?.Equals(ARRAYLEAF_TAG) ?? false))
                    {
                        proptext.Visible = true;
                    }
                    //array of structs
                    else
                    {
                        proptext.Visible = false;
                    }
                }
                else
                {
                    addArrayElementButton.Visible = false;
                    deleteArrayElement.Visible = false;
                    arrayPropertyDropdown.Enabled = false;
                    TryParseProperty();
                    LAST_SELECTED_PROP_TYPE = NONARRAYLEAF_TAG;
                }
            }
            catch (Exception ex)
            {
                addArrayElementButton.Visible = false;
                deleteArrayElement.Visible = false;
                arrayPropertyDropdown.Enabled = false;
                proptext.Visible = setPropertyButton.Visible = setValueSeparator.Visible = false;
                Debug.WriteLine("Node name is not in correct format.");
                //name is wrong, don't attempt to continue parsing.
                LAST_SELECTED_PROP_TYPE = NONARRAYLEAF_TAG;
                return;
            }
        }

        private void TryParseProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int type = BitConverter.ToInt32(memory, pos + 8);
                int test = BitConverter.ToInt32(memory, pos + 12);
                if (test != 0 || !pcc.isName(type))
                    return;
                bool visible = false;
                switch (pcc.getNameEntry(type))
                {
                    case "IntProperty":
                    case "ObjectProperty":
                    case "NameProperty":
                    case "StringRefProperty":
                        proptext.Text = BitConverter.ToInt32(memory, pos + 24).ToString();
                        visible = true;
                        break;
                    case "FloatProperty":
                        proptext.Text = BitConverter.ToSingle(memory, pos + 24).ToString();
                        visible = true;
                        break;
                    case "BoolProperty":
                        proptext.Text = memory[pos + 24].ToString();
                        visible = true;
                        break;
                    case "StrProperty":
                        string s = "";
                        int count = BitConverter.ToInt32(memory, pos + 24);
                        pos += 28;
                        for (int i = 0; i < count; i++)
                        {
                            s += (char)memory[pos + i];
                        }
                        proptext.Text = s;
                        visible = true;
                        break;
                    case "ByteProperty":
                        int size = BitConverter.ToInt32(memory, pos + 16);
                        if (size > 1)
                        {
                            try
                            {
                                List<string> values = UnrealObjectInfo.getEnumfromProp(pcc.Exports[Index].ClassName, pcc.getNameEntry(BitConverter.ToInt32(memory, pos)));
                                if (values != null)
                                {
                                    enumDropdown.Items.Clear();
                                    enumDropdown.Items.AddRange(values.ToArray());
                                    proptext.Visible = false;
                                    setPropertyButton.Visible = setValueSeparator.Visible = enumDropdown.Visible = true;
                                    enumDropdown.SelectedItem = pcc.getNameEntry(BitConverter.ToInt32(memory, pos + 24));
                                    return;
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                        else
                        {
                            proptext.Text = memory[pos + 24].ToString();
                            visible = true;
                        }
                        break;
                }
                proptext.Visible = setPropertyButton.Visible = setValueSeparator.Visible = visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void TryParseStructProperty(int type)
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 8)
                    return;
                switch (type)
                {
                    case STRUCTLEAFFLOAT_TAG:
                        proptext.Text = BitConverter.ToSingle(memory, pos).ToString();
                        break;
                    case STRUCTLEAFBYTE_TAG:
                        proptext.Text = memory[pos].ToString();
                        break;
                    case STRUCTLEAFDEG_TAG:
                        proptext.Text = ((float)BitConverter.ToInt32(memory, pos) * 360f / 65536f).ToString();
                        break;
                }
                proptext.Visible = setPropertyButton.Visible = setValueSeparator.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void TryParseArrayProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int value = BitConverter.ToInt32(memory, pos);
                proptext.Text = value.ToString();
                proptext.Visible = setPropertyButton.Visible = setValueSeparator.Visible = true;
                deleteArrayElement.Visible = true;
                arrayPropertyDropdown.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setProperty_Click(object sender, EventArgs e)
        {
            if (hb1.SelectionStart != lastSetOffset)
            {
                return; //user manually moved cursor
            }
            if (LAST_SELECTED_PROP_TYPE == ARRAYLEAF_TAG)
            {
                setArrayProperty();
            }
            else if (LAST_SELECTED_PROP_TYPE == STRUCTLEAFFLOAT_TAG || LAST_SELECTED_PROP_TYPE == STRUCTLEAFBYTE_TAG || LAST_SELECTED_PROP_TYPE == STRUCTLEAFDEG_TAG)
            {
                setStructProperty();
            }
            else
            {
                setNonArrayProperty();
            }
        }

        private void setStructProperty()
        {
            try
            {
                int pos = lastSetOffset;
                if (memory.Length - pos < 8)
                    return;
                byte b = 0;
                float f = 0;
                switch (LAST_SELECTED_PROP_TYPE)
                {
                    case STRUCTLEAFBYTE_TAG:
                        if (byte.TryParse(proptext.Text, out b))
                        {
                            memory[pos] = b;
                            RefreshMem();
                        }
                        break;
                    case STRUCTLEAFFLOAT_TAG:
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(f));
                            RefreshMem();
                        }
                        break;
                    case STRUCTLEAFDEG_TAG:
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos, BitConverter.GetBytes(Convert.ToInt32(f * 65536f / 360f)));
                            RefreshMem();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setNonArrayProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int type = BitConverter.ToInt32(memory, pos + 8);
                int test = BitConverter.ToInt32(memory, pos + 12);
                if (test != 0 || !pcc.isName(type))
                    return;
                int i = 0;
                float f = 0;
                switch (pcc.getNameEntry(type))
                {
                    case "IntProperty":
                    case "ObjectProperty":
                    case "NameProperty":
                    case "StringRefProperty":
                        if (int.TryParse(proptext.Text, out i))
                        {
                            WriteMem(pos + 24, BitConverter.GetBytes(i));
                            RefreshMem();
                        }
                        break;
                    case "FloatProperty":
                        proptext.Text = CheckSeperator(proptext.Text);
                        if (float.TryParse(proptext.Text, out f))
                        {
                            WriteMem(pos + 24, BitConverter.GetBytes(f));
                            RefreshMem();
                        }
                        break;
                    case "BoolProperty":
                        if (int.TryParse(proptext.Text, out i) && (i == 0 || i == 1))
                        {
                            memory[pos + 24] = (byte)i;
                            RefreshMem();
                        }
                        break;
                    case "ByteProperty":
                        if (enumDropdown.Visible)
                        {
                            i = pcc.FindNameOrAdd(enumDropdown.SelectedItem as string);
                            WriteMem(pos + 24, BitConverter.GetBytes(i));
                            RefreshMem();
                        }
                        else if (int.TryParse(proptext.Text, out i) && i >= 0 && i <= 255)
                        {
                            memory[pos + 24] = (byte)i;
                            RefreshMem();
                        }
                        break;
                    case "StrProperty":
                        string s = proptext.Text;
                        int offset = pos + 24;
                        int oldSize = BitConverter.ToInt32(memory, pos + 16);
                        int oldLength = BitConverter.ToInt32(memory, offset);
                        List<byte> stringBuff = new List<byte>(s.Length);
                        for (int j = 0; j < s.Length; j++)
                        {
                            stringBuff.Add(BitConverter.GetBytes(s[j])[0]);
                        }
                        stringBuff.Add(0);
                        byte[] buff = BitConverter.GetBytes((s.Count() + 1) + 4);
                        for (int j = 0; j < 4; j++)
                            memory[offset - 8 + j] = buff[j];
                        buff = BitConverter.GetBytes(s.Count() + 1);
                        for (int j = 0; j < 4; j++)
                            memory[offset + j] = buff[j];
                        buff = new byte[memory.Length - oldLength + stringBuff.Count];
                        int startLength = offset + 4;
                        int startLength2 = startLength + oldLength;
                        for (int j = 0; j < startLength; j++)
                        {
                            buff[j] = memory[j];
                        }
                        for (int j = 0; j < stringBuff.Count; j++)
                        {
                            buff[j + startLength] = stringBuff[j];
                        }
                        startLength += stringBuff.Count;
                        for (int j = 0; j < memory.Length - startLength2; j++)
                        {
                            buff[j + startLength] = memory[j + startLength2];
                        }
                        memory = buff;

                        //bubble up size
                        uint throwaway;
                        TreeNode parent = LAST_SELECTED_NODE.Parent;
                        while (parent != null && (Convert.ToInt32(parent.Tag) == STRUCT_PROPERTY || Convert.ToInt32(parent.Tag) == ARRAY_PROPERTY))
                        {
                            if (uint.TryParse(parent.Text, out throwaway))
                            {
                                parent = parent.Parent;
                                continue;
                            }
                            updateArrayLength(Convert.ToInt32(parent.Name), 0, (stringBuff.Count + 4) - oldSize);
                            parent = parent.Parent;
                        }
                        RefreshMem();
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void setArrayProperty()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos < 16)
                    return;
                int i = 0;
                if (int.TryParse(proptext.Text, out i))
                {
                    WriteMem(pos, BitConverter.GetBytes(i));
                    RefreshMem();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void deleteArrayLeaf()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (hb1.SelectionStart != lastSetOffset)
                {
                    return; //user manually moved cursor
                }

                if (memory.Length - pos < 16) //not long enough to deal with
                    return;

                TreeNode parent = LAST_SELECTED_NODE.Parent;
                int parentOffset = Convert.ToInt32(parent.Name);

                //bubble up size
                bool firstbubble = true;
                uint throwaway;
                int leafsize = (BitConverter.ToInt32(memory, 16 + parentOffset) - 4) / BitConverter.ToInt32(memory, 24 + parentOffset);
                while (parent != null && (Convert.ToInt32(parent.Tag) == STRUCT_PROPERTY || Convert.ToInt32(parent.Tag) == ARRAY_PROPERTY))
                {
                    if (uint.TryParse(parent.Text, out throwaway))
                    {
                        parent = parent.Parent;
                        continue;
                    }
                    parentOffset = Convert.ToInt32(parent.Name);
                    if (firstbubble)
                    {
                        int leafOffset = Convert.ToInt32(LAST_SELECTED_NODE.Name);
                        if ((leafOffset - parentOffset + 28) % leafsize != 0)
                        {
                            break;
                        }
                        memory = RemoveIndices(memory, leafOffset, leafsize);
                        firstbubble = false;
                        updateArrayLength(parentOffset, -1, -leafsize);
                    }
                    else
                    {
                        updateArrayLength(parentOffset, 0, -leafsize);
                    }
                    parent = parent.Parent;
                }
                RefreshMem();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void cloneArrayStruct()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (hb1.SelectionStart != lastSetOffset)
                {
                    return; //user manually moved cursor
                }
                uint throwaway;
                TreeNode arrayRoot = LAST_SELECTED_NODE;

                int beginOffset;
                int endOffset;
                if (uint.TryParse(arrayRoot.LastNode.Text, out throwaway))
                {
                    beginOffset = Convert.ToInt32(arrayRoot.LastNode.FirstNode.Name);
                }
                else
                {
                    beginOffset = Convert.ToInt32(arrayRoot.FirstNode.Name);
                }
                endOffset = Convert.ToInt32(arrayRoot.NextNode.Name);
                int size = endOffset - beginOffset;

                List<byte> memList = memory.ToList();
                memList.InsertRange(endOffset, memList.GetRange(beginOffset, size));
                memory = memList.ToArray();
                updateArrayLength(pos, 1, size);

                //bubble up size
                TreeNode parent = LAST_SELECTED_NODE.Parent;
                while (parent != null && (Convert.ToInt32(parent.Tag) == STRUCT_PROPERTY || Convert.ToInt32(parent.Tag) == ARRAY_PROPERTY))
                {
                    if (uint.TryParse(parent.Text, out throwaway))
                    {
                        parent = parent.Parent;
                        continue;
                    }
                    updateArrayLength(Convert.ToInt32(parent.Name), 0, size);
                    parent = parent.Parent;
                }
                RefreshMem();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void addArrayLeaf()
        {
            try
            {
                int pos = (int)hb1.SelectionStart;
                int newElement;
                if (hb1.SelectionStart != lastSetOffset)
                {
                    return; //user manually moved cursor
                }
                if (!int.TryParse(proptext.Text, out newElement))
                {
                    return; //not valid element
                }
                int size = BitConverter.ToInt32(memory, pos + 16);
                int count = BitConverter.ToInt32(memory, pos + 24);
                int leafsize = 4;
                if (count > 0)
                {
                    leafsize = (size - 4) / count;
                }
                else if (arrayViewerDropdown.SelectedIndex == ARRAYSVIEW_NAMES)
                {
                    leafsize = 8;
                }
                List<byte> memList = memory.ToList();
                memList.InsertRange(pos + 24 + size, BitConverter.GetBytes(newElement));
                if (leafsize > 4)
                {
                    byte[] extrabytes = new byte[leafsize - 4];
                    memList.InsertRange(pos + 24 + size + 4, extrabytes);
                }
                memory = memList.ToArray();
                updateArrayLength(pos, 1, leafsize);

                //bubble up size
                uint throwaway;
                TreeNode parent = LAST_SELECTED_NODE.Parent;
                while (parent != null && (Convert.ToInt32(parent.Tag) == STRUCT_PROPERTY || Convert.ToInt32(parent.Tag) == ARRAY_PROPERTY))
                {
                    if (uint.TryParse(parent.Text, out throwaway))
                    {
                        parent = parent.Parent;
                        continue;
                    }
                    updateArrayLength(Convert.ToInt32(parent.Name), 0, leafsize);
                    parent = parent.Parent;
                }
                RefreshMem();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private T[] RemoveIndices<T>(T[] IndicesArray, int RemoveAt, int NumElementsToRemove)
        {
            if (RemoveAt < 0 || RemoveAt > IndicesArray.Length - 1 || NumElementsToRemove < 0 || NumElementsToRemove + RemoveAt > IndicesArray.Length - 1)
            {
                return IndicesArray;
            }
            T[] newIndicesArray = new T[IndicesArray.Length - NumElementsToRemove];

            int i = 0;
            int j = 0;
            while (i < IndicesArray.Length)
            {
                if (i < RemoveAt || i >= RemoveAt + NumElementsToRemove)
                {
                    newIndicesArray[j] = IndicesArray[i];
                    j++;
                }
                else
                {
                    //Debug.WriteLine("Skipping byte: " + i.ToString("X4"));
                }

                i++;
            }

            return newIndicesArray;
        }

        private void WriteMem(int pos, byte[] buff)
        {
            for (int i = 0; i < buff.Length; i++)
                memory[pos + i] = buff[i];
        }

        /// <summary>
        /// This removes a specific amount of bytes from the memory array at the starting position indicated and will return a new memory array with those bytes removed (not just 0'd).
        /// This will make the array smaller.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pos">Position to start removing bytes at.</param>
        /// <param name="numbytestoremove">Number of bytes to remove.</param>
        /// <returns>New memory with the removed bytes</returns>
        private T[] RemoveMem<T>(int pos, int numbytestoremove)
        {
            T[] dest = new T[memory.Length - numbytestoremove];
            if (pos > 0)
                Array.Copy(memory, 0, dest, 0, pos); //get pre-removed bytes

            if (pos < memory.Length - numbytestoremove)
                Array.Copy(memory, pos + numbytestoremove, dest, pos, memory.Length - pos - numbytestoremove - 1); //append post-removed bytes

            return dest;
        }

        private T[] AddMem<T>(int pos, T[] datatoadd)
        {
            T[] dest = new T[memory.Length + datatoadd.Length];
            if (pos > 0)
                Array.Copy(memory, 0, dest, 0, pos); //get pre-insert bytes

            Array.Copy(datatoadd, 0, dest, pos, datatoadd.Length);

            if (pos < memory.Length/* + datatoadd.Length*/)
                Array.Copy(memory, pos + datatoadd.Length, dest, pos + datatoadd.Length, memory.Length + datatoadd.Length - 1); //append post-insert bytes

            return dest;
        }

        /// <summary>
        /// Updates an array properties length and size in bytes. Does not refresh the memory view
        /// </summary>
        /// <param name="startpos">Starting index of the array property</param>
        /// <param name="countDelta">Delta in terms of how many items the array has</param>
        /// <param name="byteDelta">Delta in terms of how many bytes the array data is</param>
        private void updateArrayLength(int startpos, int countDelta, int byteDelta)
        {
            int sizeOffset = 16;
            int countOffset = 24;
            int oldSize = BitConverter.ToInt32(memory, sizeOffset + startpos);
            int oldCount = BitConverter.ToInt32(memory, countOffset + startpos);

            int newSize = oldSize + byteDelta;
            int newCount = oldCount + countDelta;

            WriteMem(startpos + sizeOffset, BitConverter.GetBytes(newSize));
            WriteMem(startpos + countOffset, BitConverter.GetBytes(newCount));

        }


        private void RefreshMem()
        {
            pcc.Exports[Index].Data = memory;
            hb1.ByteProvider = new DynamicByteProvider(memory);
            StartScan();
        }

        private string CheckSeperator(string s)
        {
            string seperator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            string wrongsep;
            if (seperator == ".")
                wrongsep = ",";
            else
                wrongsep = ".";
            return s.Replace(wrongsep, seperator);
        }

        private void expandAllButton_Click(object sender, EventArgs e)
        {
            if (treeView1 != null)
            {
                treeView1.ExpandAll();
            }
        }

        private void collapseAllButton_Click(object sender, EventArgs e)
        {
            if (treeView1 != null)
            {
                treeView1.CollapseAll();
                treeView1.Nodes[0].Expand();
            }
        }

        private void arrayViewerDropdown_selectionChanged(object sender, EventArgs e)
        {
            if (previousArrayView > -1)
            {
                StartScan();
            }
            previousArrayView = arrayViewerDropdown.SelectedIndex;
        }

        private void arrayRemove4Bytes_Click(object sender, EventArgs e)
        {
            if (hb1.SelectionStart != lastSetOffset || LAST_SELECTED_NODE == null || !LAST_SELECTED_NODE.Tag.Equals(ARRAYLEAF_TAG))
            {
                return; //user manually moved cursor or we have an invalid state
            }
            try
            {
                int pos = (int)hb1.SelectionStart;
                if (memory.Length - pos - 4 < 16) //4 bytes
                    return;
                memory = RemoveMem<byte>(pos, 4);
                int off = Convert.ToInt32(LAST_SELECTED_NODE.Parent.Name);
                updateArrayLength(off, -1, -4);
                RefreshMem();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void deleteArrayElement_Click(object sender, EventArgs e)
        {
            deleteArrayLeaf();
        }

        private void addArrayElementButton_Click(object sender, EventArgs e)
        {
            if (proptext.Visible)
            {
                addArrayLeaf();
            }
            else
            {
                cloneArrayStruct();
            }
        }
    }
}
