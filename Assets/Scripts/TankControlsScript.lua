-- Math calculations:
local zero = FixedPoint.new(0)
local one = FixedPoint.new(1)
local hundreds = FixedPoint.new(1) / 100

local rotationY = 360 -- Player's current angle
local moveSpeed = one / 256

local player = Entity.Find("PlayerModel")

-- Tenths of Sines values between 10-90 degrees.
local sinTenths = {
    hundreds * 17, -- [0] 0.17 -> 10°
    hundreds * 34, -- [1] 0.34 -> 20°
    hundreds * 50, -- [2] 0.5  -> 30°
    hundreds * 64, -- [3] 0.64 -> 40°
    hundreds * 77, -- [4] 0.77 -> 50°
    hundreds * 87, -- [5] 0.87 -> 60°
    hundreds * 94, -- [6] 0.94 -> 70°
    hundreds * 98, -- [7] 0.98 -> 80°
    one,           -- [8] 1    -> 90°
}

-- Tenths of Cosines values between 10-90 degrees.
local cosTenths = {
    hundreds * 98, -- [0] 0.98 -> 10°
    hundreds * 94, -- [1] 0.94 -> 20°
    hundreds * 87, -- [2] 0.87 -> 30°
    hundreds * 77, -- [3] 0.77 -> 40°
    hundreds * 64, -- [4] 0.64 -> 50°
    hundreds * 50, -- [5] 0.5  -> 60°
    hundreds * 34, -- [6] 0.34 -> 70°
    hundreds * 17, -- [7] 0.17 -> 80°
    zero,          -- [8] 0    -> 90°
}

function onCreate(self)
    player = Entity.Find("PlayerModel")
    setPlayerRotation()
    SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
end

function onUpdate(self, dt)
    if Input.IsHeld(Input.LEFT) then
        rotationY = rotationY - 10
        if rotationY <= 0 then rotationY = 360 end
        setPlayerRotation()
    elseif Input.IsHeld(Input.RIGHT) then
        rotationY = rotationY + 10
        if rotationY >= 360 then rotationY = 10 end
        setPlayerRotation()
    end

    if Input.IsHeld(Input.UP) then
        movePlayer(true)
    elseif Input.IsHeld(Input.DOWN) then
        movePlayer(false)
    end
end

function setPlayerRotation()
    local rot = one * rotationY / 180
    Entity.SetRotationY(player, rot) -- Rotate object.
end

function movePlayer(isForward)
    local x = zero
    local z = zero
    local index = (rotationY / 10)

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

    Debug.Log("Index: " .. index .. " Angle: " .. rotationY)

    local step = moveSpeed
    if not isForward then step = -moveSpeed / 2 end

    local forward = Vec3.new(x, zero, z)
    local newPos = Vec3.mul(forward, step)
    Entity.SetPosition(player, Vec3.add(player.position, newPos))
end

function onButtonPress(self, button)
    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
    elseif button == Input.CROSS then
        -- Run speed.
        moveSpeed = one / 128
    end
end

function onButtonRelease(self, button)
    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
    elseif button == Input.CROSS then
        -- Walk speed.
        moveSpeed = one / 256
    end
end
