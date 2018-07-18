﻿using System;
using System.Collections.Generic;
using System.Text;
using GameLogic.Core;
using GameLogic.Core.ScriptSystem;
using GameLogic.EventSystem;

namespace GameLogic.CharacterSystem
{

    public class Stunt : IProperty
    {
        private sealed class API : IJSAPI
        {
            private readonly Stunt _outer;

            public API(Stunt outer)
            {
                _outer = outer;
            }

            public IJSContextProvider Origin(JSContextHelper proof)
            {
                try
                {
                    if (proof == JSContextHelper.Instance)
                    {
                        return _outer;
                    }
                    return null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private readonly API _apiObj;

        protected string _name = "";
        protected string _description = "";
        protected Character _belong = null;
        protected Command _command;
        protected bool _dmCheck = false;

        public Stunt(Command command = null, string description = "")
        {
            _command = command;
            _description = description ?? throw new ArgumentNullException(nameof(description));
            _apiObj = new API(this);
        }

        public string Name { get => _name; set => _name = value ?? throw new ArgumentNullException(nameof(Name)); }
        public string Description { get => _description; set => _description = value ?? throw new ArgumentNullException(nameof(Description)); }
        public Character Belong { get => _belong; set => _belong = value; }
        public Command Command { get => _command; set => _command = value; }
        public bool DMCheck { get => _dmCheck; set => _dmCheck = value; }

        public virtual object GetContext()
        {
            return _apiObj;
        }

        public void SetContext(object context) { }
    }
}