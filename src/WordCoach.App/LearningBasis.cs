using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WordCoach.App;

public partial class MainWindow
{
    private StackPanel LearningBasis()
    {
        var panel = Stack(Txt("哪些来自研究，哪些是软件的初始设置", 18, "#253039", true),
            Space(Txt("研究支持：在不同日期反复学习，并在看答案前主动回忆；复习间隔需要结合希望记住多久来考虑。研究没有给所有高中生规定统一的每日词数。", 13, "#63707A")),
            Space(Txt("本程序初始设置：每天 20 分钟，新词上限 5 条；周一至周六逐日安排，周日默认不自动加新词，用于到期复习和消化积压。新档案周目标为 30 条，旧档案保留原目标。这些是可调整的产品起点。", 13, "#63707A")),
            Space(Txt("先给到期回忆和补学留出时间，再决定今天的新词量。学习耗时逐步校准估计，最近延迟回忆较弱时减少新词；样本不足不自动加量。完成这批以后，由你主动选择是否加练。", 13, "#63707A")),
            Space(Txt("初学后默认次日回忆。独立答对逐步延长间隔，模糊与忘记缩短间隔并补学。1、3、7、14、30、60、90 天是可解释的起步规则，不是测得的个人遗忘曲线，也不是论文证明的唯一最佳序列。", 13, "#63707A")));
        void Link(string label, string url)
        {
            var text = Txt("", 12, "#287C81");
            var link = new Hyperlink(new Run(label)) { NavigateUri = new Uri(url) };
            link.RequestNavigate += (_, e) => { Guard(() => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })); e.Handled = true; };
            text.Inlines.Add(link); panel.Children.Add(Space(text, 8));
        }
        Link("Karpicke & Roediger, 2008 · 外语词汇的主动提取实验", "https://doi.org/10.1126/science.1152408");
        Link("Cepeda 等, 2008 · 间隔与延迟保持的关系", "https://pubmed.ncbi.nlm.nih.gov/19076480/");
        Link("Rawson & Dunlosky, 2011 · 跨会话反复提取与效率", "https://pubmed.ncbi.nlm.nih.gov/21707204/");
        return panel;
    }
}
