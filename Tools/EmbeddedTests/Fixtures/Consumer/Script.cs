namespace Fixture;
//跨模组继承，交换加载顺序时也必须绑定到同一 BaseScript。
public class DerivedScript : BaseScript { }
//内容 Mod 的归属必须仍为 B。
public sealed class ConsumerMod : Verse.Mod
{
    public ConsumerMod(Verse.ModContentPack content) : base(content) { }
}
