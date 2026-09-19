using System.Linq;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.ViewModels;
using Xunit;

namespace FluentTaskScheduler.Tests
{
    public class TriggerEditorViewModelTests
    {
        [Fact]
        public void Initially_Nothing_ExceptStartTime_IsVisible()
        {
            var vm = new TriggerEditorViewModel();

            Assert.False(vm.IsDetailsVisible);
            Assert.True(vm.IsStartTimeVisible);
            Assert.False(vm.IsDailyVisible);
            Assert.False(vm.IsWeeklyVisible);
            Assert.False(vm.IsMonthlyVisible);
            Assert.False(vm.IsEventVisible);
            Assert.False(vm.IsIdleVisible);
            Assert.False(vm.IsSessionStateVisible);
        }

        [Theory]
        [InlineData(TriggerType.Daily, true)]
        [InlineData(TriggerType.Weekly, true)]
        [InlineData(TriggerType.Monthly, true)]
        [InlineData(TriggerType.AtLogon, true)]
        [InlineData(TriggerType.AtStartup, true)]
        [InlineData(TriggerType.Once, true)]
        [InlineData(TriggerType.Event, false)]
        [InlineData(TriggerType.OnIdle, false)]
        [InlineData(TriggerType.SessionStateChange, false)]
        public void StartTime_IsHidden_OnlyForEventIdleAndSessionTriggers(TriggerType type, bool startTimeVisible)
        {
            var vm = new TriggerEditorViewModel();
            vm.ShowTrigger(type);

            Assert.True(vm.IsDetailsVisible);
            Assert.Equal(startTimeVisible, vm.IsStartTimeVisible);
        }

        [Fact]
        public void ShowTrigger_ShowsExactlyOneTypePanel()
        {
            var vm = new TriggerEditorViewModel();

            vm.ShowTrigger(TriggerType.Weekly);
            Assert.True(vm.IsWeeklyVisible);
            Assert.False(vm.IsDailyVisible);
            Assert.False(vm.IsMonthlyVisible);

            vm.ShowTrigger(TriggerType.Event);
            Assert.True(vm.IsEventVisible);
            Assert.False(vm.IsWeeklyVisible);
        }

        [Fact]
        public void Details_StayVisible_WhenTypeBecomesUnknown()
        {
            var vm = new TriggerEditorViewModel();
            vm.ShowTrigger(TriggerType.Daily);
            vm.ShowTrigger(null);

            Assert.True(vm.IsDetailsVisible);
            Assert.False(vm.IsDailyVisible);
            Assert.True(vm.IsStartTimeVisible);
        }

        [Fact]
        public void AddTrigger_AppendsDailyTrigger_AndReturnsItsIndex()
        {
            var vm = new TriggerEditorViewModel();
            vm.Triggers.Add(new TaskTriggerModel { TriggerType = TriggerType.Weekly });

            int index = vm.AddTrigger();

            Assert.Equal(1, index);
            Assert.Equal(2, vm.Triggers.Count);
            Assert.Equal(TriggerType.Daily, vm.Triggers[1].TriggerType);
            Assert.False(string.IsNullOrEmpty(vm.Triggers[1].ScheduleInfo));
        }

        [Fact]
        public void RemoveTrigger_IgnoresInvalidIndex()
        {
            var vm = new TriggerEditorViewModel();
            vm.Triggers.Add(new TaskTriggerModel());

            vm.RemoveTrigger(-1);
            vm.RemoveTrigger(5);
            Assert.Single(vm.Triggers);

            vm.RemoveTrigger(0);
            Assert.Empty(vm.Triggers);
        }

        [Fact]
        public void MoveUpAndDown_ReorderTriggers_AndReturnNewIndex()
        {
            var vm = new TriggerEditorViewModel();
            var a = new TaskTriggerModel { TriggerType = TriggerType.Daily };
            var b = new TaskTriggerModel { TriggerType = TriggerType.Weekly };
            var c = new TaskTriggerModel { TriggerType = TriggerType.Monthly };
            foreach (var t in new[] { a, b, c }) vm.Triggers.Add(t);

            Assert.Equal(0, vm.MoveUp(1));
            Assert.Equal(new[] { b, a, c }, vm.Triggers.ToArray());

            Assert.Equal(2, vm.MoveDown(1));
            Assert.Equal(new[] { b, c, a }, vm.Triggers.ToArray());
        }

        [Fact]
        public void Move_AtTheEnds_DoesNothing()
        {
            var vm = new TriggerEditorViewModel();
            vm.Triggers.Add(new TaskTriggerModel());
            vm.Triggers.Add(new TaskTriggerModel());

            Assert.Equal(0, vm.MoveUp(0));
            Assert.Equal(1, vm.MoveDown(1));
            Assert.Equal(-1, vm.MoveUp(-1));
        }
    }
}
