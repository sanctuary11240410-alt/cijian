using WordCoach.Core;

namespace WordCoach.App.Services;

/// <summary>App-authored teaching material; does not overwrite the user's imported source.</summary>
public static class TeachingSupplements
{
    private static readonly Dictionary<string,(string Meaning,string[] Phrases,string Example)> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["manufacturing"] = ("n. 制造业；制造", ["the manufacturing industry 制造业", "manufacturing costs 生产成本"], "The town used to depend on manufacturing, but now more people work in tourism."),
        ["representative"] = ("n. 代表；adj. 有代表性的；典型的", ["a class representative 班级代表", "be representative of 能代表……"], "Our class representative collected everyone's ideas before the meeting."),
        ["disadvantage"] = ("n. 不利条件；劣势；缺点", ["at a disadvantage 处于不利地位", "advantages and disadvantages 优点和缺点"], "Living far from school can be a disadvantage, but the quiet evenings help me study."),
        ["underground"] = ("adj. 地下的；adv. 在地下；n. 地铁（尤用于英式英语）", ["an underground station 地铁站", "travel by underground 乘地铁出行"], "We took the underground because the roads were crowded."),
        ["determination"] = ("n. 决心；毅力；确定", ["show determination 表现出决心", "the determination to do sth 做某事的决心"], "Her determination to finish the race kept her moving after everyone else had stopped."),
        ["heartbreaking"] = ("adj. 令人心碎的；令人悲痛的", ["heartbreaking news 令人悲痛的消息", "a heartbreaking story 令人心碎的故事"], "It was heartbreaking to see the old library close after so many years."),
        ["accommodation"] = ("n. 住处；住宿", ["student accommodation 学生宿舍", "provide accommodation 提供住宿"], "The university offers accommodation to students who live far away."),
        ["concentration"] = ("n. 专心；专注；集中；浓度", ["lose concentration 分心", "require concentration 需要专注"], "The noise outside made it difficult to keep my concentration."),
        ["investigation"] = ("n. 调查；研究", ["carry out an investigation 进行调查", "under investigation 正在调查中"], "The students carried out an investigation into the amount of food wasted at lunch."),
        ["qualification"] = ("n. 资格；学历；资历；合格条件", ["a teaching qualification 教师资格", "the qualifications for a job 任职资格"], "She worked hard to gain the qualifications needed to become a teacher."),
        ["consideration"] = ("n. 考虑；体谅；需要考虑的因素", ["take sth into consideration 把某事考虑在内", "show consideration for sb 体谅某人"], "We should take the weather into consideration before choosing a date for the trip."),
        ["extraordinary"] = ("adj. 非凡的；不寻常的；特别的", ["an extraordinary achievement 非凡的成就", "an extraordinary experience 不寻常的经历"], "An ordinary school project turned into an extraordinary discovery."),
        ["uncomfortable"] = ("adj. 不舒服的；不自在的", ["feel uncomfortable 感到不舒服或不自在", "an uncomfortable situation 令人尴尬的处境"], "I felt uncomfortable speaking in public until I joined the drama club."),
        ["anniversary"] = ("n. 周年纪念日", ["a wedding anniversary 结婚纪念日", "celebrate an anniversary 庆祝周年纪念日"], "The school planted a tree to celebrate its fiftieth anniversary."),
        ["architecture"] = ("n. 建筑学；建筑风格", ["modern architecture 现代建筑风格", "study architecture 学习建筑学"], "The city's architecture tells the story of its long history."),
        ["independence"] = ("n. 独立；自主", ["gain independence 获得独立", "a sense of independence 独立意识"], "Planning my own weekly schedule gave me a new sense of independence."),
        ["modification"] = ("n. 修改；调整；改动", ["make modifications to 对……作修改", "a minor modification 小幅调整"], "A small modification to the design made the machine much easier to use."),
        ["overweight"] = ("adj. 超重的；过重的", ["be overweight 超重", "overweight luggage 超重行李"], "My suitcase was overweight, so I moved two books into my backpack."),
        ["remarkable"] = ("adj. 非凡的；显著的；引人注目的", ["make remarkable progress 取得显著进步", "a remarkable achievement 非凡的成就"], "She made remarkable progress by learning a little every day."),
        ["illustrate"] = ("v. 说明；阐明；给……加插图", ["illustrate a point 说明一个观点", "illustrate sth with an example 用例子说明某事"], "The teacher used a short story to illustrate the difference between the two words."),
        ["persuade"] = ("v. 说服；使信服", ["persuade sb to do sth 说服某人做某事", "persuade sb of sth 使某人相信某事"], "We persuaded our classmates to bring reusable bottles to school."),
        ["responsibility"] = ("n. 责任；职责", ["take responsibility for 对……负责", "a sense of responsibility 责任感"], "Each group member took responsibility for one part of the project."),
        ["circumstance"] = ("n. 情况；情形；环境（常用复数）", ["under the circumstances 在这种情况下", "under no circumstances 决不"], "Under the circumstances, staying at school was the safest choice."),
        ["consequence"] = ("n. 结果；后果", ["as a consequence 因此；结果", "the consequences of ……的后果"], "He stayed up too late and, as a consequence, could hardly concentrate in class."),
        ["distinguish"] = ("v. 区分；辨别", ["distinguish A from B 区分 A 和 B", "distinguish between A and B 区分 A 与 B"], "The examples helped me distinguish the two similar meanings."),
        ["significant"] = ("adj. 重要的；有意义的；显著的", ["a significant difference 显著差异", "play a significant role 起重要作用"], "A few minutes of daily practice made a significant difference to my confidence.")
    };
    public static void Apply(IEnumerable<VocabularyWord> words)
    {
        foreach(var word in words)
        {
            if(!Entries.TryGetValue(word.Term,out var teaching))continue;
            word.Meaning=teaching.Meaning;
            word.Phrases=teaching.Phrases.ToList();
            word.Example=teaching.Example;
            word.Source += "；本软件补充的高中常见义与原创例句";
        }
    }
}
