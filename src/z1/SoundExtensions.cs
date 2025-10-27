using System;

namespace z1;

internal static class SoundExtensions
{
    public static void PlayItemSound(this ISound sound, ItemId itemId)
    {
        var soundId = SoundEffect.Item;

        switch (itemId)
        {
            case ItemId.Heart:
            case ItemId.Key:
                soundId = SoundEffect.KeyHeart;
                break;

            case ItemId.FiveRupees:
            case ItemId.Rupee:
                soundId = SoundEffect.Cursor;
                break;

            case ItemId.PowerTriforce:
                return;
        }

        sound.PlayEffect(soundId);
    }
}