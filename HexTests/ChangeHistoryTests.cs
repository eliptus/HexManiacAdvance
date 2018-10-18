﻿using HavenSoft.Gen3Hex.Model;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HavenSoft.HexTests {
   public class ChangeHistoryTests {

      private readonly ChangeHistory<List<int>> history;
      private int callCount = 0;
      private List<int> recentChanges;

      public ChangeHistoryTests() {
         history = new ChangeHistory<List<int>>(changes => {
            callCount++;
            recentChanges = changes;
            return changes.Select(i => -i).ToList();
         });
      }

      [Fact]
      public void CannotUndoByDefault() {
         Assert.False(history.Undo.CanExecute(null));
      }

      [Fact]
      public void CallingUndoWhenStackIsEmptyDoesNothing() {
         history.Undo.Execute();
         history.Redo.Execute();
         Assert.Equal(0, callCount);
      }

      [Fact]
      public void CanUndoAfterCheckingCurrentTransaction() {
         history.CurrentChange.Count();
         Assert.True(history.Undo.CanExecute(null));
         Assert.False(history.Redo.CanExecute(null));
      }

      [Fact]
      public void UndoCallsRevertDelegate() {
         history.CurrentChange.Count();
         history.Undo.Execute();
         Assert.Equal(1, callCount);
         Assert.False(history.Undo.CanExecute(null));
         Assert.True(history.Redo.CanExecute(null));
      }

      [Fact]
      public void UndoPassesInRecentChanges() {
         history.CurrentChange.Add(3);
         history.Undo.Execute();

         Assert.NotNull(recentChanges);
         Assert.Single(recentChanges);
         Assert.Equal(3, recentChanges[0]);
      }

      [Fact]
      public void RedoPassesInRecentChanges() {
         history.CurrentChange.Add(3);
         history.Undo.Execute();
         history.Redo.Execute();

         Assert.NotNull(recentChanges);
         Assert.Single(recentChanges);
         Assert.Equal(-3, recentChanges[0]);
      }

      [Fact]
      public void CannotRedoAfterNewChange() {
         history.CurrentChange.Add(3);
         history.Undo.Execute();
         history.CurrentChange.Add(4);

         Assert.True(history.Undo.CanExecute(null));
         Assert.False(history.Redo.CanExecute(null));
      }

      [Fact]
      public void ClosingEmptyChangeDoesNotAddUndoItem() {
         history.ChangeCompleted();

         Assert.False(history.Undo.CanExecute(null));
      }

      [Fact]
      public void ClosingChangesAllowsForMultipleUndos() {
         history.CurrentChange.Add(3);
         history.ChangeCompleted();
         history.CurrentChange.Add(4);

         history.Undo.Execute();
         Assert.Equal(4, recentChanges[0]);

         history.Undo.Execute();
         Assert.Equal(3, recentChanges[0]);
      }

      [Fact]
      public void UndoCanExecuteChangeFiresCorrectly() {
         var canExecuteChangeCalled = 0;
         history.Undo.CanExecuteChanged += (sender, e) => canExecuteChangeCalled++;

         history.CurrentChange.Add(3);
         Assert.Equal(1, canExecuteChangeCalled);

         history.Undo.Execute();
         Assert.Equal(2, canExecuteChangeCalled);
      }

      [Fact]
      public void RedoCanExecuteChangeFiresCorrectlyForRedoOperation() {
         var canExecuteChangeCalled = 0;
         history.Redo.CanExecuteChanged += (sender, e) => canExecuteChangeCalled++;

         history.CurrentChange.Add(3);
         Assert.Equal(0, canExecuteChangeCalled);

         history.Undo.Execute();
         Assert.Equal(1, canExecuteChangeCalled);

         history.Redo.Execute();
         Assert.Equal(2, canExecuteChangeCalled);
      }

      [Fact]
      public void RedoCanExecuteChangeFiresCorrectlyForNewChange() {
         var canExecuteChangeCalled = 0;
         history.Redo.CanExecuteChanged += (sender, e) => canExecuteChangeCalled++;

         history.CurrentChange.Add(3);
         Assert.Equal(0, canExecuteChangeCalled);

         history.Undo.Execute();
         Assert.Equal(1, canExecuteChangeCalled);

         history.CurrentChange.Add(4); // adding another change clears the redo stack
         Assert.Equal(2, canExecuteChangeCalled);
      }
   }
}
