﻿using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using HavenSoft.HexManiac.Core.ViewModels;
using HavenSoft.HexManiac.Core.ViewModels.DataFormats;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HavenSoft.HexManiac.Tests {
   public class EggMoveTests {
      private readonly byte[] data;
      private readonly PokemonModel model;
      private readonly ViewPort viewPort;
      private readonly List<string> messages = new List<string>();

      #region Setup

      public EggMoveTests() {
         data = Enumerable.Range(0, 0x200).Select(i => (byte)0xFF).ToArray();
         model = new PokemonModel(data);
         viewPort = new ViewPort("file.gba", model);

         viewPort.Goto.Execute("000080");
         viewPort.Edit("^pokenames[name\"\"8]8 \"Bob\" \"Steve\" \"Carl\" \"Sam\" \"Bryan\" \"Ryan\" \"Ian\" \"Matt\"");

         viewPort.Goto.Execute("000100");
         viewPort.Edit("^movenames[name\"\"8]8 \"Fire\" \"Water\" \"Earth\" \"Wind\" \"Light\" \"Dark\" \"Normal\" \"Magic\"");

         viewPort.Goto.Execute("000180");
         viewPort.Edit("<000000> Dead Beef 01 00 00 00 <000000>"); // limiter is at 188 for an eggrun at 000

         viewPort.Goto.Execute("000000");

         viewPort.OnMessage += (sender, e) => messages.Add(e);
      }

      private void CreateSimpleRun() {
         var token = new ModelDelta();
         model.WriteMultiByteValue(0, 2, token, EggMoveRun.MagicNumber + 2); // Carl
         model.WriteMultiByteValue(2, 2, token, 3);                          // Wind

         viewPort.Edit("^eggmoves`egg` ");
      }

      #endregion

      [Fact]
      public void CanCreateEggMoveStream() {
         viewPort.Edit("^eggmoves`egg` ");

         Assert.Equal(2, model.GetNextRun(0).Length);
      }

      [Fact]
      public void CanSeeEggMoveStreamWithCorrectFormat() {
         CreateSimpleRun();

         Assert.Equal(6, model.GetNextRun(0).Length);
         var section = (EggSection)viewPort[1, 0].Format;
         var item = (EggItem)viewPort[2, 0].Format;
         var endSection = (EggSection)viewPort[5, 0].Format;
         Assert.Equal("[Carl]", section.SectionName);
         Assert.Equal("Wind", item.ItemName);
         Assert.Equal("[]", endSection.SectionName);
      }

      [Fact]
      public void SelectionDoneInPairs() {
         CreateSimpleRun();

         viewPort.SelectionStart = new Point(2, 0); // should select "Wind"
         Assert.True(viewPort.IsSelected(new Point(3, 0)));

         viewPort.MoveSelectionStart.Execute(Direction.Right); // should select "[]"
         Assert.True(viewPort.IsSelected(new Point(4, 0)));
         Assert.True(viewPort.IsSelected(new Point(5, 0)));
      }

      [Fact]
      public void CanEditEggStreamManually() {
         CreateSimpleRun();

         viewPort.Edit("Dark ");
         Assert.Equal(5, model[0]);

         viewPort.Edit("[Bryan]");
         Assert.Equal(EggMoveRun.MagicNumber + 4, model.ReadMultiByteValue(2, 2));
      }

      [Fact]
      public void CanCopyPaste() {
         CreateSimpleRun();
         var fileSystem = new StubFileSystem();

         viewPort.SelectionStart = new Point(2, 0);
         viewPort.SelectionEnd = new Point(4, 0);
         viewPort.Copy.Execute(fileSystem);

         Assert.Equal("Wind []", fileSystem.CopyText.value);
      }

      [Fact]
      public void RunAutoExtends() {
         CreateSimpleRun();
         viewPort.SelectionStart = new Point(4, 0);
         viewPort.Edit("[Carl]");

         Assert.Equal(8, model.GetNextRun(0).Length);
         Assert.Equal(2, model.ReadMultiByteValue(0x188, 4));
      }

      [Fact]
      public void RunAutoMoves() {
         viewPort.Edit("^eggmoves`egg` ");
         model.WriteMultiByteValue(2, 2, new ModelDelta(), 0x0206);
         viewPort.Edit("[Carl]");

         Assert.Single(messages);
      }

      [Fact]
      public void RunAutoShortens() {
         CreateSimpleRun();
         viewPort.SelectionStart = new Point(2, 0);
         viewPort.Edit("[]");

         Assert.Equal(4, model.GetNextRun(0).Length);
         Assert.Equal(0, model.ReadMultiByteValue(0x188, 4));
      }
   }
}
