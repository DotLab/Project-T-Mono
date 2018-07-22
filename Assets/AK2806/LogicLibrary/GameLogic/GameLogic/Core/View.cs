﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace GameLogic.Core
{
    public sealed class View
    {
        public string id;
        public string battle;
        public string story;
    }

    public enum AnimateType
    {
        None, Shake
    }

    public struct StoryViewEffect
    {
        public static readonly StoryViewEffect INIT = new StoryViewEffect(new Vector4(1, 1, 1, 1), AnimateType.None);

        public Vector4 tint;
        public AnimateType animation;

        public StoryViewEffect(Vector4 tint, AnimateType animation)
        {
            this.tint = tint;
            this.animation = animation;
        }
    }

    public struct PortraitStyle
    {
        public static readonly PortraitStyle INIT = new PortraitStyle(0, 0);

        public int action;
        public int emotion;

        public PortraitStyle(int action, int emotion)
        {
            this.action = action;
            this.emotion = emotion;
        }
    }
}