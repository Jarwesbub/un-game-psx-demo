-- Math calculations:
local zero = FixedPoint.new(0)
local one = FixedPoint.new(1)

local moveSpeed = one / 256
local player
local playerPosY -- Player's Y-position that never changes.

-- Rotations use pi-units: 1024 internal units = pi radians = 180 degrees.
local rotationDegree = 0    -- Player's current rotation degree from 0 to 360.
local rotationRadian = zero -- Player's current rotation PI-unit from 0 to 2 (where 2=360 degrees).


function onCreate(self)
    player = Entity.Find("PlayerModel")
    playerPosY = player.position.y
    setPlayerStartRotation()
    updatePlayerRotation()
    setPlayerMoveAnimationFromInput()
end

function setPlayerStartRotation()
    local rot = Entity.GetRotationY(player)
    local fourth = one - one / 4

    if rot < zero and rot > -one then
        rotationDegree = 270
    elseif rot == zero then
        rotationDegree = 360
    elseif rot > zero and rot < fourth then
        rotationDegree = 90
    else
        rotationDegree = 180
    end
end

function updatePlayerRotation()
    rotationRadian = one * rotationDegree / 180
    Entity.SetRotationY(player, rotationRadian) -- Rotate object.
end

function onUpdate(self, dt)
    -- Don't move while UI is active.
    if isUIActive() then return end

    -- Player rotate:
    if Input.IsHeldPlayer1(Input.LEFT) then
        rotationDegree = rotationDegree - 10
        if rotationDegree <= 0 then rotationDegree = 360 end
        updatePlayerRotation()
    elseif Input.IsHeldPlayer1(Input.RIGHT) then
        rotationDegree = rotationDegree + 10
        if rotationDegree >= 360 then rotationDegree = 10 end
        updatePlayerRotation()
    end

    -- Player move:
    if Input.IsHeldPlayer1(Input.UP) then
        movePlayer(true)
    elseif Input.IsHeldPlayer1(Input.DOWN) then
        movePlayer(false)
    end
end

function movePlayer(isForward)
    local position = player.position
    local oldX = position.x
    local oldZ = position.z

    if isForward then
        Entity.MoveForward(player, moveSpeed)
    else
        Entity.MoveBackward(player, moveSpeed)
    end

    position = player.position

    if not isGlobalPlayerInBounds(position) then
        Entity.SetPosition(player, Vec3.new(oldX, playerPosY, oldZ))
        position = player.position
    end

    Player.SetPosition(position)
end

function onButtonPress(self, button)
    -- No actions when in dialogue.
    if isInDialogue() then return end

    -- Player animations:
    if button == Input.UP then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
    elseif button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
    elseif button == Input.CROSS then -- Walk faster:
        moveSpeed = one / 192
    elseif button == Input.START then -- Pause button:
        if isInPauseScreen() then
            -- Resume game:
            closePauseScreen()
            onEnablePlayerInput()
        else
            -- Pause game:
            openPauseScreen()
            onDisablePlayerInput()
        end
        return
    end

    -- Level skip button for testing:
    --if button == Input.SELECT then
    --    local scene = Scene.GetIndex() + 1
    --    if scene == 4 then return end
    --    Audio.PauseCDDA()
    --   Scene.Load(scene)
    --end
end

function onButtonRelease(self, button)
    -- No actions when in dialogue or pause.
    if isUIActive() then return end

    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
    elseif button == Input.CROSS then
        -- Walk speed.
        moveSpeed = one / 256
    end
end

-- Set player animation based on the controller input.
function setPlayerMoveAnimationFromInput()
    if Input.IsHeldPlayer1(Input.UP) or Input.IsHeldPlayer1(Input.DOWN) then
        onButtonPress(self, Input.UP)
    else
        onButtonRelease(self, Input.UP)
    end
end

-- Global player input functions:

function onEnablePlayerInput()
    Controls.SetEnabledPlayer1(true)
    setPlayerMoveAnimationFromInput()
end

function onDisablePlayerInput()
    Controls.SetEnabledPlayer1(false)
    SkinnedAnim.Stop("PlayerModel")
end

_G.onEnablePlayerInput = onEnablePlayerInput
_G.onDisablePlayerInput = onDisablePlayerInput
