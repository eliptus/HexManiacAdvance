﻿using HavenSoft.ViewModel.DataFormats;

namespace HavenSoft.Gen3Hex.ViewModel {
   public class HexElement {
      public static readonly HexElement Undefined = new HexElement(0, HavenSoft.ViewModel.DataFormats.Undefined.Instance);
      public byte Value { get; set; }
      public IDataFormat Format { get; set; }

      public HexElement(byte value, IDataFormat format) => (Value, Format) = (value, format);
   }
}