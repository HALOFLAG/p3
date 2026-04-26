// JsonLogicEvaluatorTests — JsonLogic 條件評估器測試（規格 §5.3 / §7.3）。
// 涵蓋：true/false 字面、變數取值、缺失變數安全降級、運算符（==, <, and, or, !, in）、
//      巢狀 var 路徑（flag.foo, hero.hp）、空 rule 視為 true。
using System.Text.Json.Nodes;
using CardNarrative.Core.Events;
using FluentAssertions;

namespace CardNarrative.Tests.Events;

public class JsonLogicEvaluatorTests
{
    private static JsonNode Rule(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void NullRule_ReturnsTrue_AsUnconditional()
    {
        var ev = new JsonLogicEvaluator();
        ev.Evaluate(null, new JsonLogicContext()).Should().BeTrue();
    }

    [Fact]
    public void LiteralTrue_ReturnsTrue()
    {
        var ev = new JsonLogicEvaluator();
        ev.Evaluate(Rule("true"), new JsonLogicContext()).Should().BeTrue();
    }

    [Fact]
    public void LiteralFalse_ReturnsFalse()
    {
        var ev = new JsonLogicEvaluator();
        ev.Evaluate(Rule("false"), new JsonLogicContext()).Should().BeFalse();
    }

    [Fact]
    public void EqualityOperator_OnVariable_ReturnsExpected()
    {
        var ev = new JsonLogicEvaluator();
        var ctx = new JsonLogicContext().Set("turn", 5);

        ev.Evaluate(Rule("{\"==\":[{\"var\":\"turn\"},5]}"), ctx).Should().BeTrue();
        ev.Evaluate(Rule("{\"==\":[{\"var\":\"turn\"},6]}"), ctx).Should().BeFalse();
    }

    [Fact]
    public void LessThan_AndAnd_Compose()
    {
        var ev = new JsonLogicEvaluator();
        var ctx = new JsonLogicContext()
            .Set("hero.hp", 4)
            .Set("hero.hpMax", 10);

        // hero.hp < hero.hpMax AND hero.hp > 0
        var rule = Rule("{\"and\":[{\"<\":[{\"var\":\"hero.hp\"},{\"var\":\"hero.hpMax\"}]},{\">\":[{\"var\":\"hero.hp\"},0]}]}");
        ev.Evaluate(rule, ctx).Should().BeTrue();
    }

    [Fact]
    public void NestedFlagPath_ResolvesViaDottedVar()
    {
        var ev = new JsonLogicEvaluator();
        var ctx = new JsonLogicContext().Set("flag.warehouse_clue_a", true);

        var rule = Rule("{\"==\":[{\"var\":\"flag.warehouse_clue_a\"},true]}");
        ev.Evaluate(rule, ctx).Should().BeTrue();
    }

    [Fact]
    public void MissingVariable_ComparisonReturnsFalse()
    {
        var ev = new JsonLogicEvaluator();
        // hero.hp 沒設 → JsonLogic var 取 null → ==5 為 false
        var rule = Rule("{\"==\":[{\"var\":\"hero.hp\"},5]}");
        ev.Evaluate(rule, new JsonLogicContext()).Should().BeFalse();
    }

    [Fact]
    public void OrOperator_ShortCircuitsToTrue()
    {
        var ev = new JsonLogicEvaluator();
        var ctx = new JsonLogicContext().Set("flag.a", true).Set("flag.b", false);
        var rule = Rule("{\"or\":[{\"var\":\"flag.a\"},{\"var\":\"flag.b\"}]}");
        ev.Evaluate(rule, ctx).Should().BeTrue();
    }

    [Fact]
    public void NotOperator_NegatesValue()
    {
        var ev = new JsonLogicEvaluator();
        var ctx = new JsonLogicContext().Set("flag.x", false);
        var rule = Rule("{\"!\":{\"var\":\"flag.x\"}}");
        ev.Evaluate(rule, ctx).Should().BeTrue();
    }

    [Fact]
    public void IfOperator_PicksBranchByCondition()
    {
        var ev = new JsonLogicEvaluator();
        var ctx = new JsonLogicContext().Set("turn", 10);
        // if (turn >= 5) then true else false
        var rule = Rule("{\"if\":[{\">=\":[{\"var\":\"turn\"},5]},true,false]}");
        ev.Evaluate(rule, ctx).Should().BeTrue();
    }

    [Fact]
    public void ContextToData_BuildsNestedObject_FlatPathsBecomeNested()
    {
        var ctx = new JsonLogicContext()
            .Set("hero.hp", 7)
            .Set("hero.attr.power", 3);

        var data = ctx.ToData();
        data["hero"]!["hp"]!.GetValue<int>().Should().Be(7);
        data["hero"]!["attr"]!["power"]!.GetValue<int>().Should().Be(3);
    }
}
