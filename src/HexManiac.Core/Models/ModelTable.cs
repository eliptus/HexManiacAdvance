﻿using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.Models.Runs.Sprites;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;

namespace HavenSoft.HexManiac.Core.Models {
   public class ModelTable : DynamicObject, IReadOnlyList<ModelArrayElement> {
      private readonly IDataModel model;
      private readonly int arrayAddress;
      private readonly Func<ModelDelta> tokenFactory;
      private readonly ITableRun run;

      public int Count => run?.ElementCount ?? 0;
      public int __len__() => Count; // for python

      public ITableRun Run => run;

      public ModelArrayElement this[int value] {
         get {
            return new ModelArrayElement(model, arrayAddress, value, tokenFactory, run);
         }
      }

      public ModelArrayElement this[string value] {
         get {
            var table = run;
            if (ArrayRunEnumSegment.TryMatch(value, table.ElementNames, out int index)) {
               return this[index];
            } else {
               throw new NotImplementedException();
            }
         }
      }

      public ModelTable(IDataModel model, ITableRun table, Func<ModelDelta> tokenFactory = null) : this(model, table.Start, tokenFactory, table) { }

      public ModelTable(IDataModel model, int address, Func<ModelDelta> tokenFactory = null, ITableRun tableRun = null) {
         (this.model, arrayAddress) = (model, address);
         run = tableRun ?? model.GetNextRun(address) as ITableRun;
         this.tokenFactory = tokenFactory ?? (() => new NoDataChangeDeltaModel());
      }

      public override bool TryGetMember(GetMemberBinder binder, out object? result) {
         result = this[binder.Name];
         return true;
      }

      public IEnumerator<ModelArrayElement> GetEnumerator() {
         var count = Count;
         for (int i = 0; i < count; i++) yield return this[i];
      }

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
   }

   public class ModelArrayElement : DynamicObject {
      private readonly IDataModel model;
      private readonly int arrayAddress;
      private readonly int arrayIndex;
      private readonly ITableRun table;
      private readonly Func<ModelDelta> tokenFactory;

      public int Start => table.Start + table.ElementLength * arrayIndex;
      public int ArrayIndex => arrayIndex;
      public int Length => table.ElementLength;
      public string Address => (table.Start + table.ElementLength * arrayIndex).ToAddress();
      public ITableRun Table => table;
      public IDataModel Model => model;
      public ModelDelta Token => tokenFactory();

      public ModelArrayElement(IDataModel model, int address, int index, Func<ModelDelta> tokenFactory, ITableRun table) {
         (this.model, arrayAddress, arrayIndex) = (model, address, index);
         this.table = table ?? (ITableRun)model.GetNextRun(arrayAddress);
         this.tokenFactory = tokenFactory;
      }

      public bool HasField(string name) => table.ElementContent.Any(field => field.Name == name);

      public string GetFieldName(int index) => table.ElementContent[index].Name;

      public string GetStringValue(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.PCS) {
            return model.TextConverter.Convert(model, valueAddress, seg.Length).Trim('"');
         } else if (seg.Type == ElementContentType.Pointer) {
            valueAddress = model.ReadPointer(valueAddress);
            if (valueAddress < 0 || valueAddress >= model.Count) return string.Empty;
            var length = PCSString.ReadString(model, valueAddress, true);
            if (length < 1) return string.Empty;
            return model.TextConverter.Convert(model, valueAddress, length).Trim('"');
         } else {
            throw new NotImplementedException();
         }
      }

      public string Serialize(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Pointer) {
            var destination = model.ReadPointer(valueAddress);
            if (destination == Pointer.NULL) return "null";
            var run = model.GetNextRun(destination);
            if (run is ArrayRun tRun) {
               run = new TableStreamRun(model, run.Start, run.PointerSources, run.FormatString,
                        tRun.ElementContent, new FixedLengthStreamStrategy(tRun.ElementCount));
            }
            if (run is IStreamRun sRun) return sRun.SerializeRun();
            return destination.ToAddress();
         }
         if (seg.Type == ElementContentType.PCS) return GetStringValue(fieldName);
         if (seg is ArrayRunEnumSegment) return GetEnumValue(fieldName);
         return GetValue(fieldName).ToString();
      }

      public int[,] GetSprite(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunPointerSegment pointerSeg) {
            var destination = model.ReadPointer(valueAddress);
            if (model.GetNextRun(destination) is not ISpriteRun spriteRun) {
               IFormattedRun tempRun = new NoInfoRun(destination, new SortedSpan<int>(valueAddress));
               model.FormatRunFactory.GetStrategy(pointerSeg.InnerFormat).TryParseData(model, string.Empty, destination, ref tempRun);
               spriteRun = tempRun as ISpriteRun;
               if (spriteRun == null) return null;
            }
            return spriteRun.GetPixels(model, 0, arrayIndex);
         } else {
            throw new NotImplementedException();
         }
      }

      public IReadOnlyList<short> GetPalette(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunPointerSegment pointerSeg) {
            var destination = model.ReadPointer(valueAddress);
            if (model.GetNextRun(destination) is not IPaletteRun palRun) {
               IFormattedRun tempRun = new NoInfoRun(destination, new SortedSpan<int>(valueAddress));
               model.FormatRunFactory.GetStrategy(pointerSeg.InnerFormat).TryParseData(model, string.Empty, destination, ref tempRun);
               palRun = tempRun as IPaletteRun;
               if (palRun == null) return null;
            }
            return palRun.GetPalette(model, 0);
         } else {
            throw new NotImplementedException();
         }
      }

      public int GetValue(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Integer) {
            return model.ReadMultiByteValue(valueAddress, seg.Length);
         } else {
            return model.ReadMultiByteValue(valueAddress, seg.Length.LimitToRange(0, 3));
         }
      }

      public int GetValue(int fieldIndex) {
         var elementOffset = table.ElementContent.Take(fieldIndex).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent[fieldIndex];
         if (seg.Type == ElementContentType.Integer) {
            return model.ReadMultiByteValue(valueAddress, seg.Length);
         } else {
            throw new NotImplementedException();
         }
      }

      public bool TryGetValue(string fieldName, out int value) {
         value = -1;
         if (!HasField(fieldName)) return false;
         value = GetValue(fieldName);
         return true;
      }

      public int GetAddress(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.FirstOrDefault(segment => segment.Name == fieldName);
         if (seg == null) return Pointer.NULL;
         if (seg.Type == ElementContentType.Pointer) {
            return model.ReadPointer(valueAddress);
         } else if (seg.Type == ElementContentType.Integer && seg.Length == 4) {
            return model.ReadPointer(valueAddress); // user wants a pointer, read it like a pointer
         } else {
            throw new NotImplementedException();
         }
      }

      public string GetEnumValue(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunEnumSegment enumSeg) {
            using (ModelCacheScope.CreateScope(model)) {
               return enumSeg.ToText(model, valueAddress, false);
            }
         } else {
            throw new NotImplementedException();
         }
      }

      public ModelTupleElement GetTuple(string fieldName) {
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         return new ModelTupleElement(model, table, arrayIndex, (ArrayRunTupleSegment)seg, tokenFactory);
      }

      public object this[string fieldName] {
         get {
            var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
            if (seg is ArrayRunEnumSegment) return GetEnumValue(fieldName);
            if (seg.Type == ElementContentType.Pointer) {
               var address = GetAddress(fieldName);
               if (model.GetNextRun(address) is ITableRun table1) {
                  return new ModelTable(model, table1.Start, tokenFactory);
               }
               return address;
            }
            if (seg.Type == ElementContentType.PCS) return GetStringValue(fieldName);
            if (seg is ArrayRunTupleSegment tuple) {
               return new ModelTupleElement(model, table, arrayIndex, tuple, tokenFactory);
            }
            return GetValue(fieldName);
         }
         set {
            var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
            if (seg is ArrayRunEnumSegment) SetEnumValue(fieldName, (string)value);
            else if (seg.Type == ElementContentType.Pointer) {
               if (value is string str) {
                  SetStringValue(fieldName, str);
               } else {
                  SetAddress(fieldName, (int)value);
               }
            } else if (seg.Type == ElementContentType.PCS) SetStringValue(fieldName, (string)value);
            else SetValue(fieldName, (int)value);
         }
      }

      public void SetEnumValue(string fieldName, string value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunEnumSegment enumSeg) {
            enumSeg.Write(null, model, tokenFactory(), valueAddress, ref value);
            table.NotifyChildren(Model, tokenFactory(), arrayIndex, table.ElementContent.IndexOf(seg));
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetAddress(string fieldName, int value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Pointer) {
            model.UpdateArrayPointer(Token, seg, table.ElementContent, arrayIndex, valueAddress, value);
         } else {
            // it's not a pointer so don't update any formats to think that this points to them
            // bet we should still update the value.
            model.WritePointer(Token, valueAddress, value);
         }
      }

      public void SetStringValue(string fieldName, string value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         var token = tokenFactory();
         if (seg.Type == ElementContentType.PCS) {
            var bytes = model.TextConverter.Convert(value, out var _);
            while (bytes.Count > seg.Length) bytes.RemoveAt(bytes.Count - 1);
            bytes[bytes.Count - 1] = 0xFF;
            while (bytes.Count < seg.Length) bytes.Add(0);
            token.ChangeData(model, valueAddress, bytes);
         } else if (seg.Type == ElementContentType.Pointer) {
            valueAddress = model.ReadPointer(valueAddress);
            var length = PCSString.ReadString(model, valueAddress, true);
            var bytes = model.TextConverter.Convert(value, out var _);
            var pcsRun = (PCSRun)model.GetNextRun(valueAddress);
            pcsRun = model.RelocateForExpansion(token, pcsRun, bytes.Count);
            pcsRun = (PCSRun)pcsRun.DeserializeRun(value, token, out var _, out var _);
            model.ObserveRunWritten(token, pcsRun);
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetValue(string fieldName, int value) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg.Type == ElementContentType.Integer || seg.Type == ElementContentType.BitArray) {
            model.WriteMultiByteValue(valueAddress, seg.Length, tokenFactory(), value);
            table.NotifyChildren(Model, tokenFactory(), arrayIndex, table.ElementContent.IndexOf(seg));
         } else {
            throw new NotImplementedException();
         }
      }

      public void SetValue(int fieldIndex, int value) {
         var elementOffset = table.ElementContent.Take(fieldIndex).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         var seg = table.ElementContent[fieldIndex];
         if (seg.Type == ElementContentType.Integer || seg.Type == ElementContentType.BitArray) {
            model.WriteMultiByteValue(valueAddress, seg.Length, tokenFactory(), value);
         } else {
            throw new NotImplementedException();
         }
      }

      public bool TryGetSubTable(string fieldName, out ModelTable table) {
         table = GetSubTable(fieldName);
         return table != null;
      }

      public ModelTable GetSubTable(string fieldName) {
         var elementOffset = table.ElementContent.Until(segment => segment.Name == fieldName).Sum(segment => segment.Length);
         var valueAddress = table.Start + table.ElementLength * arrayIndex + elementOffset;
         if (valueAddress < 0 || valueAddress >= model.Count) return null;
         var seg = table.ElementContent.Single(segment => segment.Name == fieldName);
         if (seg is ArrayRunPointerSegment pointerSeg) {
            var destination = model.ReadPointer(valueAddress);
            if (destination < 0 || destination >= model.Count) return null;
            var error = ArrayRun.TryParse(model, fieldName, pointerSeg.InnerFormat, destination, SortedSpan.One(valueAddress), table.ElementContent, out var childTable);
            if (error.HasError) {
               childTable = model.GetNextRun(destination) as ITableRun;
            }
            if (childTable == null) {
               // one last try, with validation turned off
               error = ArrayRun.TryParse(model, fieldName, pointerSeg.InnerFormat, destination, SortedSpan.One(valueAddress), table.ElementContent, false, out childTable);
               if (error.HasError) return null;
            }
            return new ModelTable(model, destination, tokenFactory, childTable);
         } else {
            return null; // format didn't match expected, give up
         }
      }

      #region DynamicObject

      public override bool TryGetMember(GetMemberBinder binder, out object? result) {
         result = null;
         var seg = table.ElementContent.FirstOrDefault(segment => segment.Name == binder.Name);
         if (seg == null) {
            throw new ArgumentException($"Couldn't find a member named {binder.Name}. Available members include: {", ".Join(table.ElementContent.Select(s => s.Name))}");
         }
         result = this[seg.Name];
         return true;
      }

      public override bool TrySetMember(SetMemberBinder binder, object? value) {
         var seg = table.ElementContent.FirstOrDefault(segment => segment.Name == binder.Name);
         if (seg == null) return base.TrySetMember(binder, value);
         this[seg.Name] = value;
         return true;
      }

      public override string ToString() {
         var result = new StringBuilder("{ ");
         bool first = true;
         foreach (var seg in table.ElementContent) {
            if (!first) result.Append(", ");
            first = false;
            result.Append(seg.Name + ": " + this[seg.Name]);
         }
         result.Append(" }");
         return result.ToString();
      }

      #endregion
   }

   public class ModelTupleElement : DynamicObject {
      private readonly IDataModel model;
      private readonly ITableRun table;
      private readonly int arrayIndex;
      private readonly ArrayRunTupleSegment tuple;
      private readonly Func<ModelDelta> tokenFactory;

      public ModelTupleElement(IDataModel model, ITableRun table, int arrayIndex, ArrayRunTupleSegment tuple, Func<ModelDelta> tokenFactory) {
         this.model = model;
         this.table = table;
         this.arrayIndex = arrayIndex;
         this.tuple = tuple;
         this.tokenFactory = tokenFactory;
      }

      public object this[string fieldName] {
         get => GetValue(fieldName);
         set => SetValue(fieldName, (int)value);
      }

      public bool HasField(string name) => tuple.Elements.Any(field => field.Name == name);

      public int GetValue(string fieldName) {
         var tup = tuple.Elements.Single(seg => seg.Name == fieldName);
         var start = table.Start + table.ElementLength * arrayIndex;
         start += table.ElementContent.Until(seg => seg == tuple).Sum(seg => seg.Length);
         var bitOffset = tuple.Elements.Until(seg => seg == tup).Sum(seg => seg.BitWidth);
         return tup.Read(model, start, bitOffset);
      }

      public void SetValue(string fieldName, int value) {
         var tup = tuple.Elements.Single(seg => seg.Name == fieldName);
         var start = table.Start + table.ElementLength * arrayIndex;
         start += table.ElementContent.Until(seg => seg == tuple).Sum(seg => seg.Length);
         var bitOffset = tuple.Elements.Until(seg => seg == tup).Sum(seg => seg.BitWidth);
         tup.Write(model, tokenFactory(), start, bitOffset, value);
      }

      #region DynamicObject

      public override bool TryGetMember(GetMemberBinder binder, out object? result) {
         result = null;
         var seg = tuple.Elements.FirstOrDefault(segment => segment.Name == binder.Name);
         if (seg == null) {
            throw new ArgumentException($"Couldn't find a member named {binder.Name}. Available members include: {", ".Join(table.ElementContent.Select(s => s.Name))}");
         }
         result = this[seg.Name];
         return true;
      }

      public override bool TrySetMember(SetMemberBinder binder, object? value) {
         var seg = tuple.Elements.FirstOrDefault(segment => segment.Name == binder.Name);
         if (seg == null) return base.TrySetMember(binder, value);
         this[seg.Name] = value;
         return true;
      }

      public override string ToString() {
         var result = new StringBuilder("{ ");
         bool first = true;
         foreach (var seg in tuple.Elements) {
            if (!first) result.Append(", ");
            first = false;
            result.Append(seg.Name + ": " + this[seg.Name]);
         }
         result.Append(" }");
         return result.ToString();
      }

      #endregion
   }

   public class AnchorGroup : DynamicObject {
      private readonly IDataModel model;
      private readonly Func<ModelDelta> tokenFactory;
      private readonly string header;

      public static IDictionary<string, AnchorGroup> GetTopLevelAnchorGroups(IDataModel model, Func<ModelDelta> tokenFactory) {
         var topLevel = new HashSet<string>();
         foreach (var anchor in model.Anchors) topLevel.Add(anchor.Split('.')[0]);
         var results = new Dictionary<string, AnchorGroup>();
         foreach (var top in topLevel) {
            results.Add(top, new(model, tokenFactory, top));
         }
         return results;
      }

      public AnchorGroup(IDataModel model, Func<ModelDelta> tokenFactory, string header) {
         this.model = model;
         this.tokenFactory = tokenFactory;
         this.header = header;
      }

      public override bool TryGetMember(GetMemberBinder binder, out object? result) {
         var name = header + "." + binder.Name;
         var address = model.GetAddressFromAnchor(tokenFactory(), -1, name);
         if (address < 0) {
            result = new AnchorGroup(model, tokenFactory, name);
            return true;
         }
         var run = model.GetNextRun(address);
         if (run is ITableRun table) {
            result = new ModelTable(model, table, tokenFactory);
            return true;
         }
         // TODO

         return base.TryGetMember(binder, out result);
      }
   }
}
