package dev.lantern.keycloak;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.util.Optional;
import java.util.ServiceLoader;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.keycloak.models.GroupModel;
import org.keycloak.protocol.ProtocolMapper;

class OutletIdMapperTest {

    private static GroupModel group(String outletId) {
        GroupModel g = mock(GroupModel.class);
        when(g.getFirstAttribute(OutletIdMapper.ATTRIBUTE)).thenReturn(outletId);
        return g;
    }

    @Test
    void returnsOutletIdOfTheOutletGroup() {
        assertEquals(Optional.of("BGS"), OutletIdMapper.resolveOutletId(Stream.of(group(null), group("BGS"))));
    }

    @Test
    void emptyWhenNoGroupCarriesAnOutletId() {
        assertEquals(Optional.empty(), OutletIdMapper.resolveOutletId(Stream.of(group(null), group(null))));
    }

    @Test
    void skipsBlankValues() {
        assertEquals(Optional.of("KLC"), OutletIdMapper.resolveOutletId(Stream.of(group("  "), group("KLC"))));
    }

    @Test
    void isRegisteredAsAProtocolMapper() {
        assertTrue(ServiceLoader.load(ProtocolMapper.class).stream()
                .anyMatch(p -> p.type() == OutletIdMapper.class));
    }
}
