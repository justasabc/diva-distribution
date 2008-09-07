/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OpenMetaverse;
using log4net;
using Nwc.XmlRpc;
using OpenSim.Data;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Cache;

namespace OpenSim.Grid.UserServer
{
    public delegate void UserLoggedInAtLocation(UUID agentID, UUID sessionID, UUID RegionID,
                                                ulong regionhandle, float positionX, float positionY, float positionZ,
                                                string firstname, string lastname);

    public class UserLoginService : LoginService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected IInterServiceInventoryServices m_inventoryService;

        public event UserLoggedInAtLocation OnUserLoggedInAtLocation;

        private UserLoggedInAtLocation handlerUserLoggedInAtLocation;

        public UserConfig m_config;

        public UserLoginService(
            UserManagerBase userManager, IInterServiceInventoryServices inventoryService,
            LibraryRootFolder libraryRootFolder,
            UserConfig config, string welcomeMess)
            : base(userManager, libraryRootFolder, welcomeMess)
        {
            m_config = config;
            m_inventoryService = inventoryService;
        }

        public override void LogOffUser(UserProfileData theUser, string message)
        {
            RegionProfileData SimInfo;
            try
            {
                SimInfo = RegionProfileData.RequestSimProfileData(
                    theUser.CurrentAgent.Handle, m_config.GridServerURL,
                    m_config.GridSendKey, m_config.GridRecvKey);

                if (SimInfo == null)
                {
                    m_log.Error("[GRID]: Region user was in isn't currently logged in");
                    return;
                }
            }
            catch (Exception)
            {
                m_log.Error("[GRID]: Unable to look up region to log user off");
                return;
            }

            // Prepare notification
            Hashtable SimParams = new Hashtable();
            SimParams["agent_id"] = theUser.ID.ToString();
            SimParams["region_secret"] = theUser.CurrentAgent.SecureSessionID.ToString();
            //SimParams["region_secret"] = SimInfo.regionSecret;
            //m_log.Info(SimInfo.regionSecret);
            SimParams["regionhandle"] = theUser.CurrentAgent.Handle.ToString();
            SimParams["message"] = message;
            ArrayList SendParams = new ArrayList();
            SendParams.Add(SimParams);

            // Update agent with target sim

            m_log.InfoFormat(
                "[ASSUMED CRASH]: Telling region {0} @ {1},{2} ({3}) that their agent is dead: {4}",
                SimInfo.regionName, SimInfo.regionLocX, SimInfo.regionLocY, SimInfo.httpServerURI,
                theUser.FirstName + " " + theUser.SurName);

            try
            {
                XmlRpcRequest GridReq = new XmlRpcRequest("logoff_user", SendParams);
                XmlRpcResponse GridResp = GridReq.Send(SimInfo.httpServerURI, 6000);

                if (GridResp.IsFault)
                {
                    m_log.ErrorFormat(
                        "[LOGIN]: XMLRPC request for {0} failed, fault code: {1}, reason: {2}, This is likely an old region revision.",
                        SimInfo.httpServerURI, GridResp.FaultCode, GridResp.FaultString);
                }
            }
            catch (Exception)
            {
                m_log.Error("[LOGIN]: Error telling region to logout user!");
            }

            //base.LogOffUser(theUser);
        }

        //public override void LogOffUser(UserProfileData theUser)
        //{

        //}

        /// <summary>
        /// Customises the login response and fills in missing values.
        /// </summary>
        /// <param name="response">The existing response</param>
        /// <param name="theUser">The user profile</param>
        /// <param name="startLocationRequest">Destination of the user</param>
        public override void CustomiseResponse(LoginResponse response, UserProfileData theUser,
                                               string startLocationRequest)
        {
            bool tryDefault = false;
            //CFK: Since the try is always "tried", the "Home Location" message should always appear, so comment this one.
            //CFK: m_log.Info("[LOGIN]: Load information from the gridserver");

            try
            {
                RegionProfileData SimInfo;
                RegionProfileData HomeInfo;
                int start_x = -1;
                int start_y = -1;
                int start_z = -1;

                // use the homeRegionID if it is stored already. If not, use the regionHandle as before
                if (theUser.HomeRegionID != UUID.Zero)
                {
                    HomeInfo =
                        RegionProfileData.RequestSimProfileData(
                            theUser.HomeRegionID, m_config.GridServerURL,
                            m_config.GridSendKey, m_config.GridRecvKey);
                }
                else
                {
                    HomeInfo =
                        RegionProfileData.RequestSimProfileData(
                            theUser.HomeRegion, m_config.GridServerURL,
                            m_config.GridSendKey, m_config.GridRecvKey);
                }

                if (startLocationRequest == "last")
                {
                    SimInfo =
                        RegionProfileData.RequestSimProfileData(
                            theUser.CurrentAgent.Handle, m_config.GridServerURL,
                            m_config.GridSendKey, m_config.GridRecvKey);
                }
                else if (startLocationRequest == "home")
                {
                    SimInfo = HomeInfo;
                }
                else
                {
                    string[] startLocationRequestParsed = Util.ParseStartLocationRequest(startLocationRequest);
                    m_log.Info("[DEBUGLOGINPARSE]: 1:" + startLocationRequestParsed[0] + ", 2:" +
                               startLocationRequestParsed[1] + ", 3:" + startLocationRequestParsed[2] + ", 4:" +
                               startLocationRequestParsed[3]);
                    if (startLocationRequestParsed[0] == "last")
                    {
                        SimInfo =
                            RegionProfileData.RequestSimProfileData(
                                theUser.CurrentAgent.Handle, m_config.GridServerURL,
                                m_config.GridSendKey, m_config.GridRecvKey);
                    }
                    else
                    {
                        m_log.Info("[LOGIN]: Looking up Sim: " + startLocationRequestParsed[0]);
                        SimInfo =
                            RegionProfileData.RequestSimProfileData(
                                startLocationRequestParsed[0], m_config.GridServerURL,
                                m_config.GridSendKey, m_config.GridRecvKey);

                        if (SimInfo == null)
                        {
                            m_log.Info("[LOGIN]: Didn't find region with a close name match sending to home location");
                            SimInfo = HomeInfo;
                        }
                        else
                        {
                            start_x = Convert.ToInt32(startLocationRequestParsed[1]);
                            start_y = Convert.ToInt32(startLocationRequestParsed[2]);
                            start_z = Convert.ToInt32(startLocationRequestParsed[3]);
                        }
                    }
                }

                // Customise the response
                //CFK: This is redundant and the next message should always appear.
                //CFK: m_log.Info("[LOGIN]: Home Location");
                if (HomeInfo != null)
                {
                    response.Home =
                        string.Format(
                            "{{'region_handle':[r{0},r{1}], 'position':[r{2},r{3},r{4}], 'look_at':[r{5},r{6},r{7}]}}",
                            (HomeInfo.regionLocX*Constants.RegionSize),
                            (HomeInfo.regionLocY*Constants.RegionSize),
                            theUser.HomeLocation.X, theUser.HomeLocation.Y, theUser.HomeLocation.Z,
                            theUser.HomeLookAt.X, theUser.HomeLookAt.Y, theUser.HomeLookAt.Z);
                }
                else
                {
                    // Emergency mode: Home-region isn't available, so we can't request the region info.
                    // Use the stored home regionHandle instead.
                    // NOTE: If the home-region moves, this will be wrong until the users update their user-profile again
                    ulong regionX = theUser.HomeRegion >> 32;
                    ulong regionY = theUser.HomeRegion & 0xffffffff;
                    response.Home =
                        string.Format(
                            "{{'region_handle':[r{0},r{1}], 'position':[r{2},r{3},r{4}], 'look_at':[r{5},r{6},r{7}]}}",
                            regionX, regionY,
                            theUser.HomeLocation.X, theUser.HomeLocation.Y, theUser.HomeLocation.Z,
                            theUser.HomeLookAt.X, theUser.HomeLookAt.Y, theUser.HomeLookAt.Z);
                    m_log.InfoFormat("[LOGIN] Home region of user {0} {1} is not available; using computed region position {2} {3}",
                                     theUser.FirstName, theUser.SurName,
                                     regionX, regionY);
                }

                // Destination
                //CFK: The "Notifying" message always seems to appear, so subsume the data from this message into
                //CFK: the next one for X & Y and comment this one.
                //CFK: m_log.Info("[LOGIN]: CUSTOMISERESPONSE: Region X: " + SimInfo.regionLocX +
                //CFK: "; Region Y: " + SimInfo.regionLocY);
                response.SimAddress = Util.GetHostFromURL(SimInfo.serverURI).ToString();
                response.SimPort = uint.Parse(SimInfo.serverURI.Split(new char[] {'/', ':'})[4]);
                response.RegionX = SimInfo.regionLocX;
                response.RegionY = SimInfo.regionLocY;

                //Not sure if the + "/CAPS/" should in fact be +"CAPS/" depending if there is already a / as part of httpServerURI
                string capsPath = Util.GetRandomCapsPath();
                response.SeedCapability = SimInfo.httpServerURI + "CAPS/" + capsPath + "0000/";

                m_log.DebugFormat(
                    "[LOGIN]: Sending new CAPS seed url {0} to client {1}",
                    response.SeedCapability, response.AgentID);

                // Notify the target of an incoming user
                //CFK: The "Notifying" message always seems to appear, so subsume the data from this message into
                //CFK: the next one for X & Y and comment this one.
                //CFK: m_log.Info("[LOGIN]: " + SimInfo.regionName + " (" + SimInfo.serverURI + ")  " +
                //CFK:    SimInfo.regionLocX + "," + SimInfo.regionLocY);

                theUser.CurrentAgent.Region = SimInfo.UUID;
                theUser.CurrentAgent.Handle = SimInfo.regionHandle;
                if (start_x >= 0 && start_y >= 0 && start_z >= 0)
                {
                    Vector3 tmp_v = new Vector3(start_x, start_y, start_z);
                    theUser.CurrentAgent.Position = tmp_v;
                }
                // Prepare notification
                Hashtable SimParams = new Hashtable();
                SimParams["session_id"] = theUser.CurrentAgent.SessionID.ToString();
                SimParams["secure_session_id"] = theUser.CurrentAgent.SecureSessionID.ToString();
                SimParams["firstname"] = theUser.FirstName;
                SimParams["lastname"] = theUser.SurName;
                SimParams["agent_id"] = theUser.ID.ToString();
                SimParams["circuit_code"] = (Int32) Convert.ToUInt32(response.CircuitCode);
                SimParams["startpos_x"] = theUser.CurrentAgent.Position.X.ToString();
                SimParams["startpos_y"] = theUser.CurrentAgent.Position.Y.ToString();
                SimParams["startpos_z"] = theUser.CurrentAgent.Position.Z.ToString();
                SimParams["regionhandle"] = theUser.CurrentAgent.Handle.ToString();
                SimParams["caps_path"] = capsPath;
                ArrayList SendParams = new ArrayList();
                SendParams.Add(SimParams);

                // Update agent with target sim

                m_log.InfoFormat(
                    "[LOGIN]: Telling region {0} @ {1},{2} ({3}) to expect user connection",
                    SimInfo.regionName, response.RegionX, response.RegionY, SimInfo.httpServerURI);

                XmlRpcRequest GridReq = new XmlRpcRequest("expect_user", SendParams);
                XmlRpcResponse GridResp = GridReq.Send(SimInfo.httpServerURI, 6000);

                if (GridResp.IsFault)
                {
                    m_log.ErrorFormat(
                        "[LOGIN]: XMLRPC request for {0} failed, fault code: {1}, reason: {2}",
                        SimInfo.httpServerURI, GridResp.FaultCode, GridResp.FaultString);
                }
                if (!GridResp.IsFault)
                {
                    bool responseSuccess = true;

                    if (GridResp.Value != null)
                    {
                        Hashtable resp = (Hashtable) GridResp.Value;
                        if (resp.ContainsKey("success"))
                        {
                            if ((string) resp["success"] == "FALSE")
                            {
                                responseSuccess = false;
                                tryDefault = true;
                            }
                        }
                    }

                    if (responseSuccess)
                    {
                        handlerUserLoggedInAtLocation = OnUserLoggedInAtLocation;
                        if (handlerUserLoggedInAtLocation != null)
                        {
                            //m_log.Info("[LOGIN]: Letting other objects know about login");
                            handlerUserLoggedInAtLocation(theUser.ID, theUser.CurrentAgent.SessionID,
                                                          theUser.CurrentAgent.Region,
                                                          theUser.CurrentAgent.Handle, theUser.CurrentAgent.Position.X,
                                                          theUser.CurrentAgent.Position.Y,
                                                          theUser.CurrentAgent.Position.Z,
                                                          theUser.FirstName, theUser.SurName);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[LOGIN]: Requested region for login not available, {0}", e);
                tryDefault = true;
            }

            if (tryDefault)
            {
                // Send him to default region instead
                // Load information from the gridserver

                ulong defaultHandle = (((ulong) m_config.DefaultX*Constants.RegionSize) << 32) |
                                      ((ulong) m_config.DefaultY*Constants.RegionSize);

                m_log.Warn(
                    "[LOGIN]: Sending user to default region " + defaultHandle + " instead");

                try
                {
                    RegionProfileData SimInfo = RegionProfileData.RequestSimProfileData(
                        defaultHandle, m_config.GridServerURL,
                        m_config.GridSendKey, m_config.GridRecvKey);

                    // Customise the response
                    m_log.Info("[LOGIN]: Home Location");
                    response.Home =
                        string.Format(
                            "{{'region_handle':[r{0},r{1}], 'position':[r{2},r{3},r{4}], 'look_at':[r{5},r{6},r{7}]}}",
                            (SimInfo.regionLocX*Constants.RegionSize),
                            (SimInfo.regionLocY*Constants.RegionSize),
                            theUser.HomeLocation.X, theUser.HomeLocation.Y, theUser.HomeLocation.Z,
                            theUser.HomeLookAt.X, theUser.HomeLookAt.Y, theUser.HomeLookAt.Z);


                    // Destination
                    m_log.Info("[LOGIN]: " +
                               "CUSTOMISERESPONSE: Region X: " + SimInfo.regionLocX + "; Region Y: " +
                               SimInfo.regionLocY);
                    response.SimAddress = Util.GetHostFromURL(SimInfo.serverURI).ToString();
                    response.SimPort = uint.Parse(SimInfo.serverURI.Split(new char[] {'/', ':'})[4]);
                    response.RegionX = SimInfo.regionLocX;
                    response.RegionY = SimInfo.regionLocY;

                    //Not sure if the + "/CAPS/" should in fact be +"CAPS/" depending if there is already a / as part of httpServerURI
                    string capsPath = Util.GetRandomCapsPath();
                    response.SeedCapability = SimInfo.httpServerURI + "CAPS/" + capsPath + "0000/";

                    // Notify the target of an incoming user
                    m_log.Info("[LOGIN]: Notifying " + SimInfo.regionName + " (" + SimInfo.serverURI + ")");

                    // Update agent with target sim
                    theUser.CurrentAgent.Region = SimInfo.UUID;
                    theUser.CurrentAgent.Handle = SimInfo.regionHandle;

                    // Prepare notification
                    Hashtable SimParams = new Hashtable();
                    SimParams["session_id"] = theUser.CurrentAgent.SessionID.ToString();
                    SimParams["secure_session_id"] = theUser.CurrentAgent.SecureSessionID.ToString();
                    SimParams["firstname"] = theUser.FirstName;
                    SimParams["lastname"] = theUser.SurName;
                    SimParams["agent_id"] = theUser.ID.ToString();
                    SimParams["circuit_code"] = (Int32) Convert.ToUInt32(response.CircuitCode);
                    SimParams["startpos_x"] = theUser.CurrentAgent.Position.X.ToString();
                    SimParams["startpos_y"] = theUser.CurrentAgent.Position.Y.ToString();
                    SimParams["startpos_z"] = theUser.CurrentAgent.Position.Z.ToString();
                    SimParams["regionhandle"] = theUser.CurrentAgent.Handle.ToString();
                    SimParams["caps_path"] = capsPath;
                    ArrayList SendParams = new ArrayList();
                    SendParams.Add(SimParams);

                    m_log.Info("[LOGIN]: Informing region at " + SimInfo.httpServerURI);
                    // Send
                    XmlRpcRequest GridReq = new XmlRpcRequest("expect_user", SendParams);
                    XmlRpcResponse GridResp = GridReq.Send(SimInfo.httpServerURI, 6000);

                    if (!GridResp.IsFault)
                    {
                        bool responseSuccess = true;


                        if (GridResp.Value != null)
                        {
                            Hashtable resp = (Hashtable) GridResp.Value;
                            if (resp.ContainsKey("success"))
                            {
                                if ((string) resp["success"] == "FALSE")
                                {
                                    responseSuccess = false;
                                }
                            }
                        }

                        if (responseSuccess)
                        {
                            handlerUserLoggedInAtLocation = OnUserLoggedInAtLocation;
                            if (handlerUserLoggedInAtLocation != null)
                            {
                                m_log.Info("[LOGIN]: Letting other objects know about login");
                                handlerUserLoggedInAtLocation(theUser.ID, theUser.CurrentAgent.SessionID,
                                                              theUser.CurrentAgent.Region,
                                                              theUser.CurrentAgent.Handle,
                                                              theUser.CurrentAgent.Position.X,
                                                              theUser.CurrentAgent.Position.Y,
                                                              theUser.CurrentAgent.Position.Z,
                                                              theUser.FirstName, theUser.SurName);
                            }
                        }
                        else
                        {
                            response.CreateDeadRegionResponse();
                        }
                    }
                    else
                    {
                        response.CreateDeadRegionResponse();
                    }
                }

                catch (Exception e)
                {
                    m_log.Warn("[LOGIN]: Default region also not available");
                    m_log.Warn("[LOGIN]: " + e);
                }
            }
        }

        // See LoginService
        protected override InventoryData GetInventorySkeleton(UUID userID)
        {
            m_log.DebugFormat(
                "[LOGIN]: Contacting inventory service at {0} for inventory skeleton of user {1}",
                m_config.InventoryUrl, userID);

            List<InventoryFolderBase> folders = m_inventoryService.GetInventorySkeleton(userID);

            if (null == folders || folders.Count == 0)
            {
                m_log.InfoFormat(
                    "[LOGIN]: A root inventory folder for user {0} was not found.  Requesting creation.", userID);

                // Although the create user function creates a new agent inventory along with a new user profile, some
                // tools are creating the user profile directly in the database without creating the inventory.  At
                // this time we'll accomodate them by lazily creating the user inventory now if it doesn't already
                // exist.
                if (!m_inventoryService.CreateNewUserInventory(userID))
                {
                    throw new Exception(
                        String.Format(
                            "The inventory creation request for user {0} did not succeed."
                            + "  Please contact your inventory service provider for more information.",
                            userID));
                }
                m_log.InfoFormat("[LOGIN]: A new inventory skeleton was successfully created for user {0}", userID);

                folders = m_inventoryService.GetInventorySkeleton(userID);
            }

            if (folders != null && folders.Count > 0)
            {
                UUID rootID = UUID.Zero;
                ArrayList AgentInventoryArray = new ArrayList();
                Hashtable TempHash;

                foreach (InventoryFolderBase InvFolder in folders)
                {
//                    m_log.DebugFormat("[LOGIN]: Received agent inventory folder {0}", InvFolder.name);

                    if (InvFolder.ParentID == UUID.Zero)
                    {
                        rootID = InvFolder.ID;
                    }
                    TempHash = new Hashtable();
                    TempHash["name"] = InvFolder.Name;
                    TempHash["parent_id"] = InvFolder.ParentID.ToString();
                    TempHash["version"] = (Int32) InvFolder.Version;
                    TempHash["type_default"] = (Int32) InvFolder.Type;
                    TempHash["folder_id"] = InvFolder.ID.ToString();
                    AgentInventoryArray.Add(TempHash);
                }

                return new InventoryData(AgentInventoryArray, rootID);
            }
            throw new Exception(
                String.Format(
                    "A root inventory folder for user {0} could not be retrieved from the inventory service",
                    userID));
        }
    }
}
