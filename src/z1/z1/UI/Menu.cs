namespace z1.UI;

internal abstract class Menu
{
    public abstract void Update();
    public abstract void Draw();
}

internal sealed class GameMenu : Menu
{
    private readonly Game game;

    public ProfileSummarySnapshot summaries;
    public int selectedIndex;

    public GameMenu(Game game, ProfileSummarySnapshot summaries)
    {
        this.game = game;
        this.summaries = summaries;
    }

    public override void Update()
    {
        if (Input.IsButtonPressing(Button.Select))
        {
            SelectNext();
            game.Sound.Play(SoundEffect.Cursor);
        }
        else if (Input.IsButtonPressing(Button.Start))
        {
            // TODO if (selectedIndex < 3)
            // TODO     StartWorld(selectedIndex);
            // TODO else if (selectedIndex == 3)
            // TODO     game.World.RegisterFile(summaries);
            // TODO else if (selectedIndex == 4)
            // TODO     game.World.EliminateFile(summaries);
        }
    }

    public override void Draw()
    {
        // Graphics::Begin();
        //
        // al_clear_to_color(al_map_rgb(0, 0, 0));
        //
        // DrawBox(0x18, 0x40, 0xD0, 0x90);
        //
        // DrawString(SelectStr, sizeof SelectStr, 0x40, 0x28, 0);
        // DrawString(NameStr, sizeof NameStr, 0x50, 0x40, 0);
        // DrawString(LifeStr, sizeof LifeStr, 0x98, 0x40, 0);
        // DrawString(RegisterStr, sizeof RegisterStr, 0x30, 0xA8, 0);
        // DrawString(EliminateStr, sizeof EliminateStr, 0x30, 0xB8, 0);
        //
        // int y = 0x58;
        // for (int i = 0; i < 3; i++)
        // {
        //     ProfileSummary & summary = summaries->Summaries[i];
        //     if (summary.IsActive())
        //     {
        //         uint8_t numBuf[3] = "";
        //         NumberToStringR(summary.Deaths, NumberSign_None, numBuf, sizeof numBuf);
        //         DrawString(numBuf, sizeof numBuf, 0x48, y + 8, 0);
        //         DrawString(summary.Name, summary.NameLength, 0x48, y, 0);
        //         uint totalHearts = summary.HeartContainers;
        //         uint heartsValue = Profile::GetMaxHeartsValue(totalHearts);
        //         DrawHearts(heartsValue, totalHearts, 0x90, y + 8);
        //         DrawFileIcon(0x30, y, summary.Quest);
        //     }
        //     DrawChar(Char_Minus, 0x88, y, 0);
        //     y += 24;
        // }
        //
        // if (selectedIndex < 3)
        //     y = 0x58 + selectedIndex * 24 + 5;
        // else
        //     y = 0xA8 + (selectedIndex - 3) * 16;
        // DrawChar(Char_FullHeart, 0x28, y, 7);
        //
        // Graphics::End();
    }


    private void SelectNext()
    {
        do
        {
            selectedIndex++;
            if (selectedIndex >= 5) selectedIndex = 0;
        } while (selectedIndex < 3 && !summaries.Summaries[selectedIndex].IsActive());
    }
}

internal sealed class EliminateMenu : Menu
{
    private readonly Game game;

    public ProfileSummarySnapshot summaries;
    public int selectedIndex;

    public EliminateMenu(Game game, ProfileSummarySnapshot summaries)
    {
        this.game = game;
        this.summaries = summaries;
    }

    public override void Draw()
    {
        throw new NotImplementedException();
    }

    public override void Update()
    {
        throw new NotImplementedException();
    }
}

internal sealed class RegisterMenu : Menu
{
    private readonly Game game;

    public ProfileSummarySnapshot summaries;
    public int selectedIndex;

    public RegisterMenu(Game game, ProfileSummarySnapshot summaries)
    {
        this.game = game;
        this.summaries = summaries;
    }

    public override void Draw()
    {
        throw new NotImplementedException();
    }

    public override void Update()
    {
        throw new NotImplementedException();
    }
}
