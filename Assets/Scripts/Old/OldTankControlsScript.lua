-- Math calculations:
local zero = FixedPoint.new(0)
local one = FixedPoint.new(1)
local hundredth = FixedPoint.new(1) / 100

local rotationY = 0 -- Player's current angle
local moveSpeed = one / 256

local player = Entity.Find("PlayerModel")

local minPosB = Entity.Find("PointA") -- Player's min position
local maxPosA = Entity.Find("PointB") -- Player's max position


-- Tenths of Sines values between 10-90 degrees.
local sinTenths = {
    hundredth * 17, -- [0] 0.17 -> 10°
    hundredth * 34, -- [1] 0.34 -> 20°
    hundredth * 50, -- [2] 0.5  -> 30°
    hundredth * 64, -- [3] 0.64 -> 40°
    hundredth * 77, -- [4] 0.77 -> 50°
    hundredth * 87, -- [5] 0.87 -> 60°
    hundredth * 94, -- [6] 0.94 -> 70°
    hundredth * 98, -- [7] 0.98 -> 80°
    one,            -- [8] 1    -> 90°
}

-- Tenths of Cosines values between 10-90 degrees.
local cosTenths = {
    hundredth * 98, -- [0] 0.98 -> 10°
    hundredth * 94, -- [1] 0.94 -> 20°
    hundredth * 87, -- [2] 0.87 -> 30°
    hundredth * 77, -- [3] 0.77 -> 40°
    hundredth * 64, -- [4] 0.64 -> 50°
    hundredth * 50, -- [5] 0.5  -> 60°
    hundredth * 34, -- [6] 0.34 -> 70°
    hundredth * 17, -- [7] 0.17 -> 80°
    zero,           -- [8] 0    -> 90°
}

function onCreate(self)
    player = Entity.Find("PlayerModel")
    minPosB = Entity.Find("PointA")
    maxPosA = Entity.Find("PointB")

    local rot = Entity.GetRotationY(player)
    local fourth = one - one / 4

    if rot < zero and rot > -one then
        rotationY = 270
    elseif rot == zero then
        rotationY = 360
    elseif rot > zero and rot < fourth then
        rotationY = 90
    else
        rotationY = 180
    end

    setPlayerRotation()
    SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
end

function setPlayerRotation()
    local rot = one * rotationY / 180
    Entity.SetRotationY(player, rot) -- Rotate object.
end

function onUpdate(self, dt)
    -- Don't move while dialogue is active.
    if isInDialogue() then return end

    -- Player rotate:
    if Input.IsHeld(Input.LEFT) then
        rotationY = rotationY - 10
        if rotationY <= 0 then rotationY = 360 end
        setPlayerRotation()
    elseif Input.IsHeld(Input.RIGHT) then
        rotationY = rotationY + 10
        if rotationY >= 360 then rotationY = 10 end
        setPlayerRotation()
    end

    -- Player move:
    if Input.IsHeld(Input.UP) then
        movePlayer(true)
    elseif Input.IsHeld(Input.DOWN) then
        movePlayer(false)
    end
end

function movePlayer(isForward)
    local x = zero
    local z = zero
    local index = rotationY / 10 -- rotation / 10

    if rotationY <= 90 then
        x = sinTenths[index]
        z = cosTenths[index]
    elseif rotationY <= 180 then
        index = index - 9
        x = cosTenths[index]
        z = -sinTenths[index]
    elseif rotationY <= 270 then
        index = index - 18
        x = -sinTenths[index]
        z = -cosTenths[index]
    else
        index = index - 27
        x = -cosTenths[index]
        z = sinTenths[index]
    end

    -- Direction
    local step = moveSpeed
    if not isForward then
        x = -x
        z = -z
        step = moveSpeed / 2
    end

    local pos = player.position
    local maxPos = maxPosA.position
    local minPos = minPosB.position

    -- Clamp movement based on maxPos and minPos transform positions.
    if (pos.x > maxPos.x and x > zero) or (pos.x < minPos.x and x < zero) then
        x = zero
    end
    if (pos.z > maxPos.z and z > zero) or (pos.z < minPos.z and z < zero) then
        z = zero
    end

    -- Set new player position.
    pos.x = pos.x + x * step
    pos.z = pos.z + z * step
    Entity.SetPosition(player, pos) --- Set new Player position
    Player.SetPosition(pos)         -- Set the PSXObject position (trigger detection).
end

function onButtonPress(self, button)
    -- Don't move while dialogue is active.
    if isInDialogue() then return end

    -- Player animations:
    if button == Input.UP then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
        -- Set moveSpeed:
        if button == Input.CROSS then moveSpeed = one / 128 end
    elseif button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
    end

    if button == Input.CROSS then
        moveSpeed = one / 192
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
    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
    elseif button == Input.CROSS then
        -- Walk speed.
        moveSpeed = one / 256
    end

    if button == Input.CROSS then
        moveSpeed = one / 256
    end
end
