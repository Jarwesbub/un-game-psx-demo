local pointA = Entity.Find("PointA")
local pointB = Entity.Find("PointB")

local cameraIndex = -1
local cameraTransforms = {
    [0] = { pos = { -20, 7, 0 }, rot = { 8, 90, 0 } },
    [1] = { pos = { -47, 12, -20 }, rot = { 20, 53, 0 } },
    [2] = { pos = { -29, 16, -26 }, rot = { 32, -34, 0 } },
    [3] = { pos = { -35, 13, -5 }, rot = { 16, -81, 0 } },
    [4] = { pos = { -64, 10, -2 }, rot = { 10, 0, 0 } },
    [5] = { pos = { -66, 2, 11 }, rot = { -11, 180, 0 } },
}

local boundIndex = -1
local boundaries = {
    [0] = { min = { x = -12, z = -7 }, max = { x = 9, z = 7 } },
    [1] = { min = { x = -79, z = -14 }, max = { x = -6, z = 14 } },
    [2] = { min = { x = -79, z = -28 }, max = { x = -53, z = 34 } },
}

-- Note: Don't use negative values in player's rotation.
local playerSpawnPositions = {
    [0] = { pos = { x = 7, y = 3, z = 0 }, rotY = 270 },    -- Game start pos.
    [1] = { pos = { x = -59, y = 3, z = 31 }, rotY = 180 }, -- Cabin door pos.
}

function onCreate(self)
    Camera.FollowPsxPlayer(false)
    pointA = Entity.Find("PointA")
    pointB = Entity.Find("PointB")
    Debug.Log("Scene1Control onCreate()")
    local fow = 280 -- Unity FOV: 55
    Camera.SetH(fow)

    local prevScene = Persist.Get("came_from") or -1
    Debug.Log("PREV SCENE: " .. prevScene)
    if prevScene == 1 then
        -- Spawn player at the cabin door.
        Audio.Play("door_close_sound", 127, 64)
        Debug.Log("Player spawns at cabin door")
        local pos = Vec3.new(playerSpawnPositions[1].pos.x, playerSpawnPositions[1].pos.y, playerSpawnPositions[1].pos.z)
        local rotY = playerSpawnPositions[1].rotY
        Debug.Log("onCreate player pos: " .. pos.x .. ", " .. pos.y .. ", " .. pos.z)
        setGlobalPlayerPosition(pos, rotY)
        setCameraPositionById(4)
    else
        -- Spawn player at the game start position.
        Debug.Log("Player spawns at start pos")
        local pos = Vec3.new(playerSpawnPositions[0].pos.x, playerSpawnPositions[0].pos.y, playerSpawnPositions[0].pos.z)
        local rotY = playerSpawnPositions[0].rotY
        Debug.Log("onCreate player pos: " .. pos.x .. ", " .. pos.y .. ", " .. pos.z)
        setGlobalPlayerPosition(pos, rotY)
        setCameraPositionById(0)
    end
end

function setCameraPositionById(number)
    if cameraIndex == number then
        do return end
    end

    local prevBoundIndex = boundIndex

    if number == 0 then
        boundIndex = 0
    elseif number == 1 or number == 2 then
        boundIndex = 1
    else
        boundIndex = 2
    end

    setCameraPositionByIndex(number)
    cameraIndex = number

    --Debug.Log("PrevBound " .. prevBoundIndex .. " current: " .. boundIndex)
    -- Set player boundaries if boundIndex does not match.
    if prevBoundIndex ~= boundIndex then
        setPlayerBoundaries(boundIndex)
    end
end

_G.setCameraPositionById = setCameraPositionById


function setCameraPositionByIndex(index)
    local pos = Vec3.new(cameraTransforms[index].pos[1], cameraTransforms[index].pos[2], cameraTransforms[index].pos[3])
    local rot = Vec3.new(cameraTransforms[index].rot[1], cameraTransforms[index].rot[2], cameraTransforms[index].rot[3])
    Debug.Log("Cam pos: " .. pos.x .. "," .. pos.y .. "," .. pos.z)
    setCameraPosition(pos, rot)
end

local hundredth = FixedPoint.new(1) / 100
function setPlayerBoundaries(index)
    local minPosX = hundredth * boundaries[index].min.x
    local minPosZ = hundredth * boundaries[index].min.z
    local maxPosX = hundredth * boundaries[index].max.x
    local maxPosZ = hundredth * boundaries[index].max.z

    Entity.SetPosition(pointA, Vec3.new(minPosX, 0, minPosZ))
    Entity.SetPosition(pointB, Vec3.new(maxPosX, 0, maxPosZ))
end
