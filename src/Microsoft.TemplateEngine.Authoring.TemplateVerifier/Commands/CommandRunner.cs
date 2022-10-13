﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.TemplateEngine.Authoring.TemplateVerifier.Commands
{
    internal class CommandRunner : ICommandRunner
    {
        public CommandResultData RunCommand(TestCommand testCommand)
        {
            return new CommandResultData(testCommand.Execute());
        }
    }
}