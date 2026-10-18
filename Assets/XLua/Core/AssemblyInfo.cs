/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The bridge (the XLua assembly of the asmdef and package layouts) uses Core's
// internal members: LuaIndexes, LuaWatchdog, LuaOptions.Clone and the UTF-8
// buffer. In the Assets layout and in XLUA_GENERAL builds both are one
// assembly and this attribute has no effect.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("XLua")]
