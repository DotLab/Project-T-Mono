﻿using System;
using System.Collections.Generic;
using System.Text;
using Jint;

namespace GameLogic.Framework.EventSystem
{
    interface IEvent
    {
        string ID
        {
            get;
        }

        void ProvideParam(Engine engine);
    }

    abstract class Event : IEvent
    {
        private string id;

        public string ID => this.id;
        
        public abstract void ProvideParam(Engine engine);
    }
}