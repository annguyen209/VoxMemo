using VoxMemo.ViewModels;

namespace VoxMemo.Tests;

public class OnboardingViewModelTests
{
    [Fact]
    public void InitialStep_IsZero()
    {
        var vm = new OnboardingViewModel();
        Assert.Equal(0, vm.CurrentStep);
    }

    [Fact]
    public void IsStep0_TrueOnlyWhenCurrentStepIsZero()
    {
        var vm = new OnboardingViewModel();
        Assert.True(vm.IsStep0);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.False(vm.IsStep3);
    }

    [Fact]
    public void ShowApiKey_FalseForOllama()
    {
        var vm = new OnboardingViewModel { SelectedAiProvider = "Ollama" };
        Assert.False(vm.ShowApiKey);
    }

    [Fact]
    public void ShowApiKey_TrueForOpenAI()
    {
        var vm = new OnboardingViewModel { SelectedAiProvider = "OpenAI" };
        Assert.True(vm.ShowApiKey);
    }

    [Fact]
    public void ShowApiKey_TrueForAnthropic()
    {
        var vm = new OnboardingViewModel { SelectedAiProvider = "Anthropic" };
        Assert.True(vm.ShowApiKey);
    }

    [Fact]
    public void ModelDownloaded_FalseInitially()
    {
        var vm = new OnboardingViewModel();
        Assert.False(vm.ModelDownloaded);
    }

    [Fact]
    public void CloseRequested_RaisedByRaiseCloseForTest()
    {
        var vm = new OnboardingViewModel();
        bool raised = false;
        vm.CloseRequested += (_, _) => raised = true;
        vm.RaiseCloseForTest();
        Assert.True(raised);
    }
}
