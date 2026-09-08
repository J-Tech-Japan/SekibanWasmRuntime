/// Type-safe event match macro.
#[macro_export]
macro_rules! match_event {
    ($event:expr, $state:ident, {
        $($event_type:ident($e:ident) => $handler:expr),* $(,)?
    }) => {{
        let event = $event;
        let $state = $state;

        match event.event_type.as_str() {
            $(
                <$event_type as $crate::event::EventPayload>::EVENT_TYPE => {
                    match event.deserialize::<$event_type>() {
                        Some($e) => $handler,
                        None => $state,
                    }
                }
            )*
            _ => $state,
        }
    }};
}

/// Domain registration macro.
#[macro_export]
macro_rules! domain_types {
    ($name:ident {
        $(events: [$($event:ty),* $(,)?],)?
        $(tags: [$($tag:ty),* $(,)?],)?
        $(tag_projectors: [$($tag_proj:ty),* $(,)?],)?
        $(multi_projectors: [$($multi_proj:ty),* $(,)?],)?
        $(commands: [$($cmd:ty),* $(,)?],)?
        $(queries: [$($query:ty),* $(,)?],)?
        $(list_queries: [$($list_query:ty),* $(,)?],)?
    }) => {
        pub struct $name;

        impl $crate::registry::DomainDefinition for $name {
            fn register(builder: &mut $crate::registry::DomainTypesBuilder) {
                $($(
                    builder.register_event::<$event>();
                )*)?

                $($(
                    builder.register_tag::<$tag>();
                )*)?

                $($(
                    builder.register_tag_projector::<$tag_proj>();
                )*)?

                $($(
                    builder.register_multi_projector::<$multi_proj>();
                )*)?

                $($(
                    builder.register_command::<$cmd>();
                )*)?

                $($(
                    builder.register_query::<$query>();
                )*)?

                $($(
                    builder.register_list_query::<$list_query>();
                )*)?
            }
        }

        impl $crate::registry::DomainProjectorRegistration for $name {
            fn register_projectors<R: $crate::registry::ProjectorRegistrar>(registrar: &mut R) {
                $($(
                    registrar.register_tag_projector::<$tag_proj>();
                )*)?

                $($(
                    registrar.register_multi_projector::<$multi_proj>();
                )*)?
            }
        }
    };
}

/// Combine multiple domains.
#[macro_export]
macro_rules! combine_domains {
    ($name:ident = $($d1:ident)::+ + $($d2:ident)::+) => {
        pub type $name = $crate::registry::CombinedDomain<$($d1)::+, $($d2)::+>;
    };
    ($name:ident = $($d1:ident)::+ + $($d2:ident)::+ $(+ $($rest:ident)::+)+) => {
        pub type $name = $crate::combine_domains!(@nested $($d1)::+ + $($d2)::+ $(+ $($rest)::+)+);
    };
    (@nested $($d1:ident)::+ + $($d2:ident)::+) => {
        $crate::registry::CombinedDomain<$($d1)::+, $($d2)::+>
    };
    (@nested $($d1:ident)::+ + $($d2:ident)::+ $(+ $($rest:ident)::+)+) => {
        $crate::registry::CombinedDomain<
            $($d1)::+,
            $crate::combine_domains!(@nested $($d2)::+ $(+ $($rest)::+)+)
        >
    };
}
